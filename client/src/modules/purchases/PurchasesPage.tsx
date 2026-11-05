import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { DataTable, type Column, type SortState } from '@/ui/DataTable'
import { DateRange } from '@/ui/DateRange'
import { formatDate, formatMoney, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { useReceiptPrinting } from '../_shared/receiptActions'
import sales from '../_shared/sales.module.css'
import { useSupplier } from '../suppliers/api'
import {
  downloadPurchasesCsv,
  PAGE_SIZES,
  readListParams,
  usePurchaseList,
  usePurchaseSummary,
  writeListParams,
  type PurchaseListItem,
  type PurchaseListParams,
} from './api'
import styles from './purchases.module.css'

/** `/app/purchases` - every purchase note, newest first, with totals per currency, filters and a CSV. */
export default function PurchasesPage() {
  const { t } = useTranslation(['purchases', 'common'])
  const navigate = useNavigate()
  const canCreate = useCan(Permissions.Purchases_Create)
  const [search, setSearch] = useSearchParams()
  const params = readListParams(search)
  const list = usePurchaseList(params)
  const summary = usePurchaseSummary(params)
  const printing = useReceiptPrinting()
  const [exporting, setExporting] = useState(false)
  const supplier = useSupplier(params.supplierId ?? 0, params.supplierId !== null)

  const update = (patch: Partial<PurchaseListParams>, resetPage = true) =>
    setSearch(writeListParams({ ...params, ...patch, page: resetPage ? 1 : (patch.page ?? params.page) }), { replace: true })

  const sort: SortState = { key: params.sort, dir: params.dir }
  const filtered = Boolean(params.q || params.status || params.from || params.to || params.supplierId)

  const exportCsv = async () => {
    setExporting(true)
    try {
      await downloadPurchasesCsv(params)
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('common.actionFailed', { ns: 'common' }))
    } finally {
      setExporting(false)
    }
  }

  const columns: Column<PurchaseListItem>[] = [
    {
      key: 'number',
      header: t('Number'),
      sortable: true,
      required: true,
      mono: true,
      cell: (p) => (
        <Link className={styles.name} to={`/purchases/${p.id}`}>
          {p.number}
        </Link>
      ),
    },
    { key: 'date', header: t('Date'), sortable: true, hideBelow: 700, cell: (p) => formatDate(p.purchaseDate) },
    {
      key: 'supplier',
      header: t('Supplier'),
      sortable: true,
      cell: (p) => (
        <Link className={styles.link} to={`/suppliers/${p.supplierId}`} onClick={(e) => e.stopPropagation()}>
          <bdi>{p.supplierName}</bdi>
        </Link>
      ),
    },
    {
      key: 'reference',
      header: t('SupplierReference'),
      sortable: true,
      hideBelow: 1000,
      cell: (p) => (p.supplierReference ? <bdi>{p.supplierReference}</bdi> : <span className={styles.muted}>-</span>),
    },
    { key: 'items', header: t('Items'), align: 'center', hideBelow: 900, cell: (p) => formatNumber(p.itemCount, 0) },
    {
      key: 'total',
      header: t('Total'),
      sortable: true,
      align: 'end',
      cell: (p) => <strong className="tabular-nums">{formatMoney(p.total, p.currencyNotation)}</strong>,
    },
    {
      key: 'due',
      header: t('LeftToPay'),
      align: 'end',
      hideBelow: 800,
      cell: (p) =>
        p.status === 'cancelled' || p.amountDue <= 0 ? (
          <span className={styles.muted}>-</span>
        ) : (
          <span className={styles.money} data-tone="owe">
            {formatMoney(p.amountDue, p.currencyNotation)}
          </span>
        ),
    },
    {
      key: 'status',
      header: t('Status'),
      sortable: true,
      align: 'center',
      cell: (p) => <StatusBadge tone={p.status === 'cancelled' ? 'danger' : 'success'} label={p.status === 'cancelled' ? t('StatusCancelled') : t('StatusReceived')} />,
    },
  ]

  const s = summary.data
  const money = (pick: 'total' | 'due') =>
    s
      ? s.totals.length
        ? s.totals.map((c) => formatMoney(pick === 'total' ? c.total : c.due, c.currencyNotation)).join(' · ')
        : formatNumber(0)
      : '–'

  return (
    <div className={sales.page}>
      <PageHeader
        title={t('Purchases')}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('NewPurchase') : undefined}
        onPrimaryAction={canCreate ? () => void navigate(params.supplierId ? `/purchases/new?supplier=${params.supplierId}` : '/purchases/new') : undefined}
      >
        <Button loading={exporting} onClick={() => void exportCsv()}>
          {t('ExportCsv')}
        </Button>
      </PageHeader>

      <div className={sales.tiles}>
        <StatTile label={t('PurchasesCount')} value={s ? formatNumber(s.count, 0) : '–'} />
        <StatTile label={t('TotalPurchased')} value={money('total')} tone="info" />
        <StatTile label={t('LeftToPay')} value={money('due')} tone="danger" />
        <StatTile label={t('StatusCancelled')} value={s ? formatNumber(s.cancelledCount, 0) : '–'} />
      </div>

      <DataTable<PurchaseListItem>
        caption={t('Purchases')}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(p) => p.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.error ? (isApiError(list.error) ? list.error.message : t('LoadFailed')) : null}
        onRetry={() => void list.refetch()}
        emptyTitle={filtered ? t('EmptyFiltered') : t('Empty')}
        emptyMessage={filtered ? undefined : t('EmptyHint')}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: 'date', dir: 'desc' }, false)}
        onRowClick={(p) => void navigate(`/purchases/${p.id}`)}
        toolbar={
          <div className={sales.filters}>
            <div className={sales.search}>
              <SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchPlaceholder')} aria-label={t('SearchPlaceholder')} />
            </div>
            <div className={sales.chips}>
              <span className={sales.chipsLabel}>{t('Status')}</span>
              <ChipGroup
                size="sm"
                aria-label={t('Status')}
                options={[
                  { value: '', label: t('AllStatuses') },
                  { value: 'received', label: t('StatusReceived') },
                  { value: 'cancelled', label: t('StatusCancelled') },
                ]}
                value={params.status}
                onChange={(status) => update({ status: status === 'received' || status === 'cancelled' ? status : '' })}
              />
            </div>
            <DateRange value={{ from: params.from, to: params.to }} onChange={({ from, to }) => update({ from, to })} />
            {params.supplierId !== null && (
              <span className={styles.filterChip}>
                {t('SupplierFilter', { name: supplier.data?.name ?? `#${params.supplierId}` })}
                <button type="button" className={styles.chipClear} aria-label={t('ClearSupplierFilter')} onClick={() => update({ supplierId: null })}>
                  ×
                </button>
              </span>
            )}
          </div>
        }
        rowActions={(p) => (
          <IconButton
            icon="print"
            label={t('PrintReceipt')}
            disabled={printing.busy}
            onClick={() => void printing.print(`/purchases/${p.id}/print`, `/purchases/${p.id}/pdf`, `${p.number}.pdf`)}
          />
        )}
        footer={
          <Pagination
            page={params.page}
            pageSize={params.pageSize}
            total={list.data?.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={(page) => update({ page }, false)}
            onPageSizeChange={(pageSize) => update({ pageSize })}
          />
        }
      />
    </div>
  )
}
