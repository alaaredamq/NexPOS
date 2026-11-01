import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useCan } from '@/core/permissions/permissions'
import { ChipGroup } from '@/ui/ChipGroup'
import { DataTable, type Column, type SortState } from '@/ui/DataTable'
import { DateRange } from '@/ui/DateRange'
import { formatDate, formatDateTime, formatMoney, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { useReceiptPrinting } from '../_shared/receiptActions'
import sales from '../_shared/sales.module.css'
import { useReceiptList, useReceiptStats, type ReceiptListItem, type ReceiptListParams, type StatusFilter } from './api'
import { PAGE_SIZES, readListParams, writeListParams, type View } from './listParams'
import { ReceiptAnalytics } from './ReceiptAnalytics'
import { VoidReceiptDialog } from './VoidReceiptDialog'

/** `/receipts`: sales tiles for a date range, the receipts list (all / valid / void), analytics, reprint and void. */
export default function ReceiptsListPage() {
  const { t } = useTranslation('receipts')
  const navigate = useNavigate()
  const [search, setSearch] = useSearchParams()
  const params = useMemo(() => readListParams(search), [search])
  const list = useReceiptList(params)
  const stats = useReceiptStats(params.from, params.to)
  const canVoid = useCan(Permissions.Sales_Edit)
  const printing = useReceiptPrinting()
  const [voiding, setVoiding] = useState<ReceiptListItem | null>(null)

  // Any filter change goes back to page 1; replace keeps typing in the search box out of the history.
  const update = (patch: Partial<ReceiptListParams & { view: View }>, resetPage = true) =>
    setSearch(writeListParams({ ...params, ...patch, page: resetPage ? 1 : (patch.page ?? params.page) }), { replace: true })

  const sort: SortState = { key: params.sort, dir: params.dir }
  const filtered = params.q !== '' || params.status !== 'all' || params.from !== '' || params.to !== ''
  const showVoid = params.status === 'void'
  const s = stats.data

  const columns: Column<ReceiptListItem>[] = [
    { key: 'number', header: `${t('Receipt')} #`, sortable: true, required: true, mono: true, cell: (r) => <strong dir="ltr">{r.receiptNumber}</strong> },
    { key: 'customer', header: t('Index.Customer'), sortable: true, cell: (r) => r.customerName ?? <span className={sales.muted}>{t('WalkInCustomer')}</span> },
    ...(showVoid
      ? ([
          { key: 'voidDate', header: t('VoidDate'), sortable: true, hideBelow: 700, cell: (r) => (r.voidDate ? formatDateTime(r.voidDate) : '–') },
          { key: 'reason', header: t('Reason'), hideBelow: 900, cell: (r) => r.voidReason || '–' },
          { key: 'voidedBy', header: t('VoidedBy'), hideBelow: 1100, cell: (r) => r.voidedBy || '–' },
        ] as Column<ReceiptListItem>[])
      : ([
          { key: 'cashier', header: t('Index.Cashier'), sortable: true, hideBelow: 1100, cell: (r) => r.cashierName || '–' },
          { key: 'date', header: t('Date'), sortable: true, hideBelow: 700, cell: (r) => formatDateTime(r.receiptDate) },
          { key: 'payment', header: t('Payment'), sortable: true, hideBelow: 900, cell: (r) => r.paymentMethod },
        ] as Column<ReceiptListItem>[])),
    {
      key: 'amount',
      header: t('Amount'),
      sortable: true,
      align: 'end',
      cell: (r) => <strong className="tabular-nums">{formatMoney(r.totalAmount, r.currencyNotation)}</strong>,
    },
    {
      key: 'status',
      header: t('Index.Status'),
      align: 'center',
      cell: (r) => <StatusBadge tone={r.isVoid ? 'danger' : 'success'} label={r.isVoid ? t('Void') : t('details.valid')} />,
    },
  ]

  const statusOptions: { value: StatusFilter; label: string }[] = [
    { value: 'all', label: t('AllReceipts') },
    { value: 'valid', label: t('details.valid') },
    { value: 'void', label: t('VoidReceipts') },
  ]
  const viewOptions: { value: View; label: string }[] = [
    { value: 'list', label: t('details.list') },
    { value: 'analytics', label: t('Analytics') },
  ]

  const period = s ? `${formatDate(s.from)} – ${formatDate(s.to)}` : undefined
  const sum = (pick: (c: NonNullable<typeof s>['currencies'][number]) => number) =>
    s ? (s.currencies.length ? s.currencies.map((c) => formatMoney(pick(c), c.currencyNotation)).join(' · ') : formatNumber(0)) : '–'

  return (
    <div className={sales.page}>
      <PageHeader title={t('ReceiptManagement')} count={list.data ? formatNumber(list.data.total, 0) : undefined}>
        <ChipGroup aria-label={t('details.view')} options={viewOptions} value={params.view} onChange={(view) => update({ view: view as View }, false)} />
      </PageHeader>

      <div className={sales.tiles}>
        <StatTile label={t('TotalSales')} value={sum((c) => c.totalSales)} sub={period} tone="success" />
        <StatTile label={t('TotalReceipts')} value={s ? formatNumber(s.validCount, 0) : '–'} sub={period} />
        <StatTile label={t('VoidReceipts')} value={s ? formatNumber(s.voidCount, 0) : '–'} sub={period} tone={s && s.voidCount > 0 ? 'danger' : 'default'} />
        <StatTile label={t('AverageReceipt')} value={sum((c) => c.averageReceipt)} sub={period} />
      </div>

      {params.view === 'analytics' ? (
        <>
          <div className={sales.filters}>
            <DateRange value={{ from: params.from, to: params.to }} onChange={({ from, to }) => update({ from, to })} />
          </div>
          {s && <ReceiptAnalytics stats={s} />}
        </>
      ) : (
        <DataTable
          caption={t('ReceiptManagement')}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(r) => r.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.error ? (isApiError(list.error) ? list.error.message : t('details.loadFailed')) : null}
          onRetry={() => void list.refetch()}
          emptyTitle={filtered ? t('details.emptyFiltered') : t('details.empty')}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: 'date', dir: 'desc' }, false)}
          columnChooserKey="nexpos.receipts.columns"
          onRowClick={(r) => navigate(`/receipts/${r.id}`)}
          toolbar={
            <div className={sales.filters}>
              <div className={sales.search}>
                <SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('ReceiptNumberCustomerNameOrPhone')} aria-label={t('SearchReceipts')} />
              </div>
              <div className={sales.chips}>
                <span className={sales.chipsLabel}>{t('Index.Status')}</span>
                <ChipGroup
                  size="sm"
                  aria-label={t('Index.Status')}
                  options={statusOptions}
                  value={params.status}
                  onChange={(status) => update({ status: status as StatusFilter, sort: 'date', dir: 'desc' })}
                />
              </div>
              <DateRange value={{ from: params.from, to: params.to }} onChange={({ from, to }) => update({ from, to })} />
            </div>
          }
          rowActions={(r) => (
            <>
              <IconButton
                icon="print"
                label={t('PrintReceipt')}
                disabled={printing.busy}
                onClick={() => void printing.print(`/receipts/${r.id}/print`, `/receipts/${r.id}/pdf`, `${r.receiptNumber}.pdf`)}
              />
              {canVoid && !r.isVoid && <IconButton icon="close" tone="danger" label={t('VoidReceipt')} onClick={() => setVoiding(r)} />}
            </>
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
      )}

      {voiding && <VoidReceiptDialog receipt={voiding} onClose={() => setVoiding(null)} />}
    </div>
  )
}
