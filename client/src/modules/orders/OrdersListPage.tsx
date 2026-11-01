import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { DataTable, type Column, type SortState } from '@/ui/DataTable'
import { DateRange } from '@/ui/DateRange'
import { formatDateTime, formatMoney, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { saveDownload, useReceiptPrinting } from '../_shared/receiptActions'
import sales from '../_shared/sales.module.css'
import { ordersApi, ORDER_STATUSES, STATUS_KEYS, STATUS_TONES, useOrderList, useOrderSummary, type OrderListItem, type OrderListParams } from './api'
import { PAGE_SIZES, readListParams, writeListParams } from './listParams'
import { OrderStatusDialog } from './OrderStatusDialog'

/** `/orders`: last-30-days tiles, filters mirrored in the URL, a server-paged table, status change, reprint and CSV export. */
export default function OrdersListPage() {
  const { t } = useTranslation('orders')
  const navigate = useNavigate()
  const [search, setSearch] = useSearchParams()
  const params = useMemo(() => readListParams(search), [search])
  const list = useOrderList(params)
  const summary = useOrderSummary()
  const canEdit = useCan(Permissions.Orders_Edit)
  const printing = useReceiptPrinting()
  const [editing, setEditing] = useState<OrderListItem | null>(null)
  const [exporting, setExporting] = useState(false)

  // Any filter change goes back to page 1; replace keeps typing in the search box out of the history.
  const update = (patch: Partial<OrderListParams>, resetPage = true) =>
    setSearch(writeListParams({ ...params, ...patch, page: resetPage ? 1 : (patch.page ?? params.page) }), { replace: true })

  const sort: SortState = { key: params.sort, dir: params.dir }
  const filtered = params.q !== '' || params.status !== '' || params.from !== '' || params.to !== '' || params.customerId !== null
  const s = summary.data

  const onExport = async () => {
    setExporting(true)
    try {
      saveDownload(await ordersApi.export(params), 'orders.csv')
    } catch (e) {
      toast.error(isApiError(e) ? e.message : t('list.exportFailed'))
    } finally {
      setExporting(false)
    }
  }

  const columns: Column<OrderListItem>[] = [
    { key: 'number', header: `${t('Order')} #`, sortable: true, required: true, mono: true, cell: (o) => <strong dir="ltr">{o.orderNumber}</strong> },
    { key: 'customer', header: t('Customer'), sortable: true, cell: (o) => o.customerName ?? <span className={sales.muted}>{t('list.walkIn')}</span> },
    { key: 'date', header: t('Date'), sortable: true, hideBelow: 700, cell: (o) => formatDateTime(o.orderDate) },
    {
      key: 'total',
      header: t('Total'),
      sortable: true,
      align: 'end',
      cell: (o) => <strong className="tabular-nums">{formatMoney(o.totalAmount, o.currencyNotation)}</strong>,
    },
    {
      key: 'status',
      header: t('Status'),
      sortable: true,
      align: 'center',
      cell: (o) => <StatusBadge tone={STATUS_TONES[o.status] ?? 'neutral'} label={t(STATUS_KEYS[o.status] ?? 'Pending')} />,
    },
    { key: 'items', header: t('Items'), sortable: true, align: 'center', hideBelow: 900, cell: (o) => formatNumber(o.itemCount, 0) },
  ]

  const statusOptions = [
    { value: '', label: t('list.allStatuses') },
    ...ORDER_STATUSES.map((st) => ({ value: String(st), label: t(STATUS_KEYS[st] as string) })),
  ]
  const period = s ? t('list.period', { 0: s.periodDays }) : undefined

  return (
    <div className={sales.page}>
      <PageHeader title={t('OrdersManagement')} count={list.data ? formatNumber(list.data.total, 0) : undefined}>
        <Button onClick={() => void Promise.all([list.refetch(), summary.refetch()])}>{t('Refresh')}</Button>
        <Button loading={exporting} onClick={() => void onExport()}>
          {t('list.export')}
        </Button>
      </PageHeader>

      <div className={sales.tiles}>
        <StatTile label={t('TotalOrders')} value={s ? formatNumber(s.totalOrders, 0) : '–'} sub={period} />
        <StatTile label={t('Delivered')} value={s ? formatNumber(s.deliveredOrders, 0) : '–'} tone="success" sub={period} />
        <StatTile label={t('Pending')} value={s ? formatNumber(s.pendingOrders, 0) : '–'} sub={period} />
        <StatTile
          label={t('TotalRevenue')}
          value={s ? (s.revenue.length ? s.revenue.map((r) => formatMoney(r.amount, r.currencyNotation)).join(' · ') : formatNumber(0)) : '–'}
          sub={period}
          tone="info"
        />
      </div>

      <DataTable
        caption={t('Orders')}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(o) => o.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.error ? (isApiError(list.error) ? list.error.message : t('list.loadFailed')) : null}
        onRetry={() => void list.refetch()}
        emptyTitle={filtered ? t('list.emptyFiltered') : t('list.empty')}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: 'date', dir: 'desc' }, false)}
        columnChooserKey="nexpos.orders.columns"
        onRowClick={(o) => navigate(`/orders/${o.id}`)}
        toolbar={
          <div className={sales.filters}>
            <div className={sales.search}>
              <SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('list.searchPlaceholder')} aria-label={t('list.searchPlaceholder')} />
            </div>
            <div className={sales.chips}>
              <span className={sales.chipsLabel}>{t('Status')}</span>
              <ChipGroup size="sm" aria-label={t('Status')} options={statusOptions} value={params.status} onChange={(status) => update({ status })} />
            </div>
            <DateRange value={{ from: params.from, to: params.to }} onChange={({ from, to }) => update({ from, to })} />
          </div>
        }
        rowActions={(o) => (
          <>
            <IconButton
              icon="print"
              label={t('PrintReceipt')}
              disabled={printing.busy}
              onClick={() => void printing.print(`/pos/sales/${o.id}/print`, `/pos/sales/${o.id}/receipt.pdf`, `${o.orderNumber}.pdf`)}
            />
            {canEdit && o.status !== 4 && <IconButton icon="edit" label={t('UpdateStatus')} onClick={() => setEditing(o)} />}
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

      {editing && <OrderStatusDialog order={editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
