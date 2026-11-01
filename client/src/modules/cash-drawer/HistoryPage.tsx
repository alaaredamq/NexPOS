import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { DataTable, type Column } from '@/ui/DataTable'
import { DateRange, type DateRangeValue } from '@/ui/DateRange'
import { formatDateTime, formatNumber, toDateInputValue } from '@/ui/format'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { StatusBadge } from '@/ui/StatusBadge'
import { useDrawerHistory, type DrawerHistoryRow } from './api'
import { money, signedMoney } from './money'
import styles from './cashDrawer.module.css'

const PAGE_SIZES = [10, 25, 50]

/** The last 30 days, today included - the Razor page's default. */
function lastThirtyDays(): DateRangeValue {
  const to = new Date()
  const from = new Date()
  from.setDate(from.getDate() - 29)
  return { from: toDateInputValue(from), to: toDateInputValue(to) }
}

/** `/app/cash-drawer/history` - the signed-in user's own sessions; a closed one opens its Z report, an open one its close page. */
export default function HistoryPage() {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const navigate = useNavigate()
  const canClose = useCan(Permissions.Sales_Edit)
  const [range, setRange] = useState<DateRangeValue>(lastThirtyDays)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const query = useDrawerHistory({ page, pageSize, from: range.from, to: range.to })

  const columns: Column<DrawerHistoryRow>[] = [
    { key: 'id', header: t('SessionId', { ns: 'cashDrawer' }), required: true, cell: (s) => `#${s.id}` },
    { key: 'opened', header: t('OpenedAt', { ns: 'cashDrawer' }), cell: (s) => formatDateTime(s.openedAt) },
    { key: 'closed', header: t('ClosedAt', { ns: 'cashDrawer' }), hideBelow: 900, cell: (s) => (s.closedAt ? formatDateTime(s.closedAt) : '-') },
    { key: 'opening', header: t('OpeningCash', { ns: 'cashDrawer' }), align: 'end', cell: (s) => money(s.openingCash) },
    { key: 'sales', header: t('TotalSales', { ns: 'cashDrawer' }), align: 'end', cell: (s) => (s.totalSales ? money(s.totalSales) : '-') },
    { key: 'count', header: t('Transactions', { ns: 'cashDrawer' }), align: 'center', hideBelow: 800, cell: (s) => (s.totalTransactions ? formatNumber(s.totalTransactions, 0) : '-') },
    {
      key: 'difference',
      header: t('CashDifference', { ns: 'cashDrawer' }),
      align: 'end',
      cell: (s) =>
        !s.cashDifference ? (
          '-'
        ) : (
          <span className={s.cashDifference > 0 ? styles.gain : styles.loss}>
            {signedMoney(s.cashDifference)}
          </span>
        ),
    },
    {
      key: 'status',
      header: t('Status', { ns: 'cashDrawer' }),
      align: 'center',
      cell: (s) => <StatusBadge tone={s.isOpen ? 'success' : 'neutral'} label={s.isOpen ? t('Open', { ns: 'cashDrawer' }) : t('Closed', { ns: 'cashDrawer' })} />,
    },
  ]

  const openRow = (s: DrawerHistoryRow) => {
    if (!s.isOpen) void navigate(`/cash-drawer/z-report/${s.id}`)
    else if (canClose) void navigate(`/cash-drawer/close/${s.id}`)
  }

  return (
    <div className={styles.page}>
      <PageHeader title={t('SessionHistory', { ns: 'cashDrawer' })} count={query.data ? formatNumber(query.data.total, 0) : undefined}>
        <Button variant="ghost" onClick={() => void navigate('/cash-drawer')}>
          {t('CashDrawerManagement', { ns: 'cashDrawer' })}
        </Button>
      </PageHeader>

      <DataTable<DrawerHistoryRow>
        caption={t('SessionHistory', { ns: 'cashDrawer' })}
        columns={columns}
        rows={query.data?.items ?? []}
        rowKey={(s) => s.id}
        loading={query.isPending}
        refreshing={query.isFetching && !query.isPending}
        error={query.isError ? t('FailedToLoadHistory', { ns: 'cashDrawer' }) : null}
        onRetry={() => void query.refetch()}
        onRowClick={openRow}
        emptyTitle={t('NoSessionsFoundForTheSelectedDateRange', { ns: 'cashDrawer' })}
        toolbar={
          <DateRange
            value={range}
            onChange={(next) => {
              setRange(next)
              setPage(1)
            }}
          />
        }
        rowActionsLabel={t('Actions', { ns: 'cashDrawer' })}
        rowActions={(s) =>
          s.isOpen ? (
            canClose ? (
              <Button onClick={() => void navigate(`/cash-drawer/close/${s.id}`)}>{t('CloseDrawer', { ns: 'cashDrawer' })}</Button>
            ) : null
          ) : (
            <Button onClick={() => void navigate(`/cash-drawer/z-report/${s.id}`)}>{t('ZReport', { ns: 'cashDrawer' })}</Button>
          )
        }
        footer={
          <Pagination
            page={page}
            pageSize={pageSize}
            total={query.data?.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={setPage}
            onPageSizeChange={(size) => {
              setPageSize(size)
              setPage(1)
            }}
          />
        }
      />
    </div>
  )
}
