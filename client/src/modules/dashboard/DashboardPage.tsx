import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDateTime, formatMoney, formatMoneyCompact, formatNumber, formatPercent, ltrIsolate } from '@/ui/format'
import { PageHeader } from '@/ui/PageHeader'
import { Skeleton } from '@/ui/Skeleton'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { useSession } from '@/core/auth/useSession'
import { flattenNav, type FlatNavItem } from '@/shell/nav'
import { CATEGORY_COLORS, categoryColor } from '../_shared/categoryPalette'
import { DonutChart } from '../_shared/charts/DonutChart'
import { TrendPanel } from '../_shared/charts/TrendPanel'
import { STATUS_KEYS, STATUS_TONES } from '../orders/api'
import { useDashboard, useSalesTrend, type Dashboard, type DashboardOrder } from './api'
import { NavTarget } from './NavTarget'
import { fillTrend, PERIODS, pointLabel, type Period } from './trend'
import styles from './dashboard.module.css'

const SALES_COLOR = CATEGORY_COLORS[0]
const PROFIT_COLOR = CATEGORY_COLORS[2]

/** `+12.5%` / `-3.0%` / `0.0%`, kept in one piece inside Arabic text. */
const signedPercent = (value: number) => ltrIsolate(`${value > 0 ? '+' : value < 0 ? '−' : ''}${formatNumber(Math.abs(value), 1)}%`)
const signedCount = (value: number) => ltrIsolate(`${value > 0 ? '+' : value < 0 ? '−' : ''}${formatNumber(Math.abs(value), 0)}`)
const toneOf = (value: number) => (value > 0 ? 'success' : value < 0 ? 'danger' : 'default')

const QUICK_ACTIONS = [
  { key: 'POS', title: 'PointOfSale', sub: 'MainPOSTerminal' },
  { key: 'Products', title: 'AddProduct', sub: 'AddNewInventory' },
  { key: 'Customers', title: 'NewCustomer', sub: 'RegisterCustomer' },
  { key: 'Reports', title: 'ViewReports', sub: 'BusinessAnalytics' },
] as const

function Tiles({ data }: { data: Dashboard | undefined }) {
  const { t } = useTranslation('dashboard')
  const notation = data?.currencyNotation ?? ''
  const dash = '-'

  return (
    <div className={styles.tiles} aria-busy={!data}>
      <StatTile
        label={t('MonthRevenue')}
        value={data ? formatMoney(data.monthSales, notation) : dash}
        tone={data ? toneOf(data.monthSalesChangePercent) : 'default'}
        sub={data ? t('VsLastMonthPercent', { value: signedPercent(data.monthSalesChangePercent) }) : undefined}
      />
      <StatTile
        label={t('MonthOrders')}
        value={data ? formatNumber(data.monthOrders, 0) : dash}
        sub={data ? t('VsLastMonthCount', { value: signedCount(data.monthOrdersChange) }) : undefined}
      />
      <StatTile
        label={t('TodaySales')}
        value={data ? formatMoney(data.todaySales, notation) : dash}
        sub={data ? t('OrdersToday', { n: formatNumber(data.todayOrders, 0) }) : undefined}
      />
      <StatTile
        label={t('MonthProfit')}
        value={data ? formatMoney(data.monthProfit, notation) : dash}
        tone={data ? toneOf(data.monthProfitChangePercent) : 'default'}
        sub={data ? t('VsLastMonthPercent', { value: signedPercent(data.monthProfitChangePercent) }) : undefined}
      />
      <StatTile label={t('TotalCustomers')} value={data ? formatNumber(data.customerCount, 0) : dash} />
      <StatTile label={t('TotalProducts')} value={data ? formatNumber(data.productCount, 0) : dash} />
    </div>
  )
}

function SalesChart() {
  const { t } = useTranslation(['dashboard', 'common'])
  const [period, setPeriod] = useState<Period>('month')
  const trend = useSalesTrend(period)
  const notation = trend.data?.currencyNotation ?? ''

  const points = useMemo(() => {
    if (!trend.data) return []
    const shown = (PERIODS as string[]).includes(trend.data.period) || trend.data.period === 'week' ? (trend.data.period as Period) : period
    return fillTrend(trend.data.points, shown).map((row) => ({ label: pointLabel(row.date, shown), values: [row.sales, row.profit] }))
  }, [trend.data, period])

  const options = PERIODS.map((p) => ({ value: p, label: t(p === 'month' ? 'Month' : p === 'quarter' ? 'Quarter' : 'Year') }))

  return (
    <section className={styles.card} aria-label={t('SalesAnalytics')}>
      <div className={styles.cardHead}>
        <h2 className={styles.cardTitle}>{t('SalesAnalytics')}</h2>
        <ChipGroup options={options} value={period} onChange={(v) => setPeriod(v as Period)} size="sm" aria-label={t('ChartPeriod')} />
      </div>
      {trend.isPending ? (
        <Skeleton height={260} />
      ) : trend.isError ? (
        <div className={styles.error} role="alert">
          <span>{t('FailedToLoadChartData')}</span>
          <Button onClick={() => void trend.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      ) : (
        <TrendPanel
          series={[
            { key: 'sales', name: t('SalesRevenue'), color: SALES_COLOR },
            { key: 'profit', name: t('Profit'), color: PROFIT_COLOR, dashed: true },
          ]}
          points={points}
          formatValue={(v) => formatMoney(v, notation)}
          formatTick={(v) => formatMoneyCompact(v, '')}
          ariaLabel={t('SalesChartLabel')}
          labelHeader={t('Date')}
          refreshing={trend.isFetching}
          emptyText={t('NoSalesInPeriod')}
        />
      )}
    </section>
  )
}

function Categories({ data }: { data: Dashboard | undefined }) {
  const { t } = useTranslation('dashboard')
  const notation = data?.currencyNotation ?? ''
  const total = data?.categories.reduce((sum, c) => sum + c.sales, 0) ?? 0

  return (
    <section className={styles.card} aria-label={t('TopCategories')}>
      <div className={styles.cardHead}>
        <h2 className={styles.cardTitle}>{t('TopCategories')}</h2>
        <span className={styles.muted}>{t('ThisMonth')}</span>
      </div>
      {!data ? (
        <Skeleton height={230} />
      ) : data.categories.length === 0 ? (
        <p className={styles.empty}>{t('NoSalesInPeriod')}</p>
      ) : (
        <DonutChart
          ariaLabel={t('CategoryChartLabel')}
          centerLabel={t('SalesRevenue')}
          centerValue={formatMoneyCompact(total, '')}
          slices={data.categories.map((c) => ({
            name: c.name,
            value: c.sales,
            color: categoryColor(c.name),
            shareText: formatPercent(c.percentage),
            valueText: formatMoney(c.sales, notation),
          }))}
        />
      )}
    </section>
  )
}

function QuickActions({ items }: { items: FlatNavItem[] }) {
  const { t } = useTranslation('dashboard')
  const available = QUICK_ACTIONS.flatMap((action) => {
    const item = items.find((i) => i.key === action.key)
    return item ? [{ ...action, item }] : []
  })
  if (available.length === 0) return null

  return (
    <section className={styles.card} aria-label={t('QuickActions')}>
      <h2 className={styles.cardTitle}>{t('QuickActions')}</h2>
      <div className={styles.actions}>
        {available.map(({ key, title, sub, item }) => (
          <NavTarget key={key} item={item} className={styles.action}>
            <strong>{t(title)}</strong>
            <span>{t(sub)}</span>
          </NavTarget>
        ))}
      </div>
    </section>
  )
}

function RecentOrders({ data, orders, query }: { data: Dashboard | undefined; orders: FlatNavItem | undefined; query: { isError: boolean; error: Error | null; refetch: () => void } }) {
  const { t } = useTranslation(['dashboard', 'orders', 'common'])

  const columns: Column<DashboardOrder>[] = [
    {
      key: 'id',
      header: t('OrderID'),
      required: true,
      cell: (o) =>
        orders ? (
          <NavTarget item={orders} spaSuffix={String(o.id)} legacySuffix={`Details/${o.id}`} className={styles.link}>
            #{o.id}
          </NavTarget>
        ) : (
          <span className={styles.name}>#{o.id}</span>
        ),
    },
    { key: 'customer', header: t('Customer'), hideBelow: 700, cell: (o) => (o.customerName ? <bdi>{o.customerName}</bdi> : <span className={styles.muted}>{t('WalkIn')}</span>) },
    { key: 'amount', header: t('Amount'), align: 'end', cell: (o) => <strong>{formatMoney(o.amount, o.currencyNotation)}</strong> },
    { key: 'date', header: t('Date'), hideBelow: 600, cell: (o) => formatDateTime(o.orderDate) },
    { key: 'payment', header: t('PaymentMethod'), hideBelow: 900, cell: (o) => (o.paymentMethod ? <bdi>{o.paymentMethod}</bdi> : <span className={styles.muted}>-</span>) },
    { key: 'status', header: t('Status'), cell: (o) => <StatusBadge tone={STATUS_TONES[o.status] ?? 'neutral'} label={t(STATUS_KEYS[o.status] ?? 'Pending', { ns: 'orders' })} /> },
  ]

  return (
    <section className={styles.card} aria-label={t('RecentOrders')}>
      <div className={styles.cardHead}>
        <h2 className={styles.cardTitle}>{t('RecentOrders')}</h2>
        {orders && (
          <NavTarget item={orders} className={styles.link}>
            {t('ViewAll')}
          </NavTarget>
        )}
      </div>
      <DataTable<DashboardOrder>
        caption={t('RecentOrders')}
        columns={columns}
        rows={data?.recentOrders ?? []}
        rowKey={(o) => o.id}
        loading={!data && !query.isError}
        error={query.isError ? (query.error?.message ?? t('FailedToLoadChartData')) : null}
        onRetry={query.refetch}
        emptyTitle={t('NoOrdersYet')}
      />
    </section>
  )
}

/** `/app/dashboard`: this month's tiles, the sales chart, sales by category, shortcuts and the latest orders. */
export default function DashboardPage() {
  const { t } = useTranslation('dashboard')
  const session = useSession()
  const nav = useMemo(() => flattenNav(session.nav), [session.nav])
  const overview = useDashboard()

  return (
    <div className={styles.page}>
      <PageHeader title={t('Dashboard')} />
      <p className={styles.subtitle}>{t('DashboardSubtitle')}</p>

      {overview.isError && !overview.data ? (
        <div className={styles.error} role="alert">
          <span>{overview.error.message}</span>
          <Button onClick={() => void overview.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      ) : (
        <Tiles data={overview.data} />
      )}

      <div className={styles.charts}>
        <SalesChart />
        <Categories data={overview.data} />
      </div>

      <QuickActions items={nav} />
      <RecentOrders
        data={overview.data}
        orders={nav.find((i) => i.key === 'Orders')}
        query={{ isError: overview.isError, error: overview.error, refetch: () => void overview.refetch() }}
      />
    </div>
  )
}
