import { useCallback, useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { DataTable, type Column } from '@/ui/DataTable'
import { DateRange, type DateRangeValue } from '@/ui/DateRange'
import { formatDate, formatMoney, formatMoneyCompact, formatNumber, formatPercent, formatQty, toDateInputValue } from '@/ui/format'
import { PageHeader } from '@/ui/PageHeader'
import { Skeleton } from '@/ui/Skeleton'
import { StatTile } from '@/ui/StatTile'
import { toast } from '@/ui/toast'
import { CATEGORY_COLORS, categoryColor } from '../_shared/categoryPalette'
import { DonutChart } from '../_shared/charts/DonutChart'
import { TrendPanel } from '../_shared/charts/TrendPanel'
import { saveDownload } from '../_shared/saveDownload'
import { reportsApi, useReportsSummary, useSalesReport, type Range, type SalesReport, type SalesReportPayment, type SalesReportProduct } from './api'
import styles from './reports.module.css'

/** `3 Oct` - the x-axis text; the tooltip and the table carry the full date. */
function shortDay(date: string): string {
  const [y, m, d] = date.split('-').map(Number)
  const lang = document.documentElement.lang || 'en'
  return new Intl.DateTimeFormat(`${lang}-u-nu-latn`, { day: 'numeric', month: 'short' }).format(new Date(y ?? 1970, (m ?? 1) - 1, d ?? 1))
}

const ISO_DAY = /^\d{4}-\d{2}-\d{2}$/

function lastDays(days: number): Range {
  const to = new Date()
  const from = new Date(to.getFullYear(), to.getMonth(), to.getDate() - (days - 1))
  return { from: toDateInputValue(from), to: toDateInputValue(to) }
}

/** The range lives in the URL (`?from=2026-10-01&to=2026-10-31`): a refresh, the back button and a copied link show the same report. */
function useRange() {
  const [search, setSearch] = useSearchParams()
  const from = search.get('from') ?? ''
  const to = search.get('to') ?? ''
  const range = useMemo<Range>(() => (ISO_DAY.test(from) && ISO_DAY.test(to) && from <= to ? { from, to } : lastDays(30)), [from, to])
  const setRange = useCallback((next: DateRangeValue) => setSearch(next.from && next.to ? { from: next.from, to: next.to } : {}, { replace: true }), [setSearch])
  return [range, setRange] as const
}

function SummaryTiles() {
  const { t } = useTranslation('reports')
  const query = useReportsSummary()
  const data = query.data
  const money = (v: number) => (data ? formatMoney(v, data.currencyNotation) : '-')
  const orders = (n: number) => (data ? t('OrdersCount', { n: formatNumber(n, 0) }) : undefined)

  return (
    <div className={styles.tiles} aria-busy={query.isPending}>
      <StatTile label={t('TodaysSales')} value={money(data?.today.sales ?? 0)} sub={orders(data?.today.orders ?? 0)} />
      <StatTile label={t('ThisMonth')} value={money(data?.thisMonth.sales ?? 0)} sub={orders(data?.thisMonth.orders ?? 0)} />
      <StatTile label={t('ThisYear')} value={money(data?.thisYear.sales ?? 0)} sub={orders(data?.thisYear.orders ?? 0)} />
      <StatTile label={t('AverageOrder')} value={money(data?.averageOrderValue ?? 0)} sub={data ? t('ThisMonthPerOrder') : undefined} />
    </div>
  )
}

/** Ranked horizontal bars: payment methods compared on one baseline, largest first. */
function PaymentBars({ rows, notation }: { rows: readonly SalesReportPayment[]; notation: string }) {
  const { t } = useTranslation('reports')
  const max = Math.max(0, ...rows.map((r) => r.sales))
  return (
    <ul className={styles.bars} aria-label={t('SalesByPaymentMethod')}>
      {rows.map((row) => (
        <li key={row.paymentMethodId} className={styles.bar}>
          <span className={styles.barLabel} title={row.name}>
            <bdi>{row.name}</bdi>
          </span>
          <span className={styles.barTrack} aria-hidden="true">
            <span className={styles.barFill} style={{ inlineSize: `${max > 0 ? Math.max(2, (row.sales / max) * 100) : 0}%`, background: CATEGORY_COLORS[0] }} />
          </span>
          <span className={`${styles.barValue} tabular-nums`}>
            {formatMoney(row.sales, notation)} <span className={styles.muted}>{formatPercent(row.percentage)}</span>
          </span>
        </li>
      ))}
    </ul>
  )
}

function Results({ report, refreshing }: { report: SalesReport; refreshing: boolean }) {
  const { t } = useTranslation('reports')
  const notation = report.currencyNotation

  const productColumns: Column<SalesReportProduct>[] = [
    { key: 'rank', header: t('Rank'), width: 56, cell: (_p, i) => i + 1 },
    { key: 'name', header: t('Product'), required: true, cell: (p) => <strong className={styles.name}><bdi>{p.name}</bdi></strong> },
    { key: 'qty', header: t('QuantitySold'), align: 'end', cell: (p) => formatQty(p.quantitySold) },
    { key: 'revenue', header: t('Revenue'), align: 'end', cell: (p) => <strong>{formatMoney(p.revenue, notation)}</strong> },
    { key: 'avg', header: t('AvgPrice'), align: 'end', hideBelow: 700, cell: (p) => formatMoney(p.averagePrice, notation) },
  ]

  const categoryTotal = report.categories.reduce((sum, c) => sum + c.sales, 0)
  const noSales = report.totalOrders === 0

  return (
    <div className={styles.results} data-refreshing={refreshing || undefined}>
      <div className={styles.tiles}>
        <StatTile label={t('TotalSales')} value={formatMoney(report.totalSales, notation)} tone="success" />
        <StatTile label={t('TotalOrders')} value={formatNumber(report.totalOrders, 0)} />
        <StatTile label={t('AverageOrderValue')} value={formatMoney(report.averageOrderValue, notation)} />
        <StatTile label={t('ItemsSold')} value={formatQty(report.totalItemsSold)} />
      </div>

      {noSales && <p className={styles.empty}>{t('NoSalesInRange')}</p>}

      <section className={styles.card} aria-label={t('DailySalesTrend')}>
        <h2 className={styles.cardTitle}>{t('DailySalesTrend')}</h2>
        <TrendPanel
          series={[{ key: 'sales', name: t('Sales'), color: CATEGORY_COLORS[0] }]}
          points={report.daily.map((d) => ({ label: shortDay(d.date), title: formatDate(d.date), values: [d.sales] }))}
          formatValue={(v) => formatMoney(v, notation)}
          formatTick={(v) => formatMoneyCompact(v, '')}
          ariaLabel={t('DailySalesTrend')}
          labelHeader={t('Date')}
          emptyText={t('NoSalesInRange')}
        />
      </section>

      {!noSales && (
        <div className={styles.split}>
          <section className={styles.card} aria-label={t('TopProducts')}>
            <h2 className={styles.cardTitle}>{t('TopProducts')}</h2>
            <DataTable<SalesReportProduct> caption={t('TopProducts')} columns={productColumns} rows={report.topProducts} rowKey={(p) => p.productId} emptyTitle={t('NoSalesInRange')} />
          </section>

          <section className={styles.card} aria-label={t('SalesByCategory')}>
            <h2 className={styles.cardTitle}>{t('SalesByCategory')}</h2>
            {report.categories.length === 0 ? (
              <p className={styles.empty}>{t('NoSalesInRange')}</p>
            ) : (
              <DonutChart
                ariaLabel={t('SalesByCategory')}
                centerLabel={t('Sales')}
                centerValue={formatMoneyCompact(categoryTotal, '')}
                slices={report.categories.map((c) => ({
                  name: c.name,
                  value: c.sales,
                  color: categoryColor(c.name),
                  shareText: formatPercent(c.percentage),
                  valueText: formatMoney(c.sales, notation),
                }))}
              />
            )}
          </section>
        </div>
      )}

      {report.paymentMethods.length > 0 && (
        <section className={styles.card} aria-label={t('SalesByPaymentMethod')}>
          <h2 className={styles.cardTitle}>{t('SalesByPaymentMethod')}</h2>
          <PaymentBars rows={report.paymentMethods} notation={notation} />
        </section>
      )}
    </div>
  )
}

/** `/app/reports`: today / month / year tiles and the sales report of a date range, with CSV and PDF export. */
export default function ReportsPage() {
  const { t } = useTranslation(['reports', 'common'])
  const [range, setRange] = useRange()
  const report = useSalesReport(range)
  const [exporting, setExporting] = useState<'csv' | 'pdf' | null>(null)

  const thisYear = () => {
    const now = new Date()
    setRange({ from: `${now.getFullYear()}-01-01`, to: toDateInputValue(now) })
  }

  const download = async (kind: 'csv' | 'pdf') => {
    setExporting(kind)
    try {
      saveDownload(await reportsApi[kind](range), `sales-report.${kind}`)
    } catch (e) {
      toast.error(isApiError(e) ? e.message : t('ExportFailed'))
    } finally {
      setExporting(null)
    }
  }

  return (
    <div className={styles.page}>
      <PageHeader title={t('ReportsAndAnalytics')}>
        <Button loading={exporting === 'csv'} disabled={exporting !== null || !report.data} onClick={() => void download('csv')}>
          {t('ExportCsv')}
        </Button>
        <Button loading={exporting === 'pdf'} disabled={exporting !== null || !report.data} onClick={() => void download('pdf')}>
          {t('DownloadPdf')}
        </Button>
      </PageHeader>

      <SummaryTiles />

      <section className={styles.filters} aria-label={t('SalesReport')}>
        <h2 className={styles.cardTitle}>{t('SalesReport')}</h2>
        <div className={styles.filterRow}>
          <DateRange value={range} onChange={setRange} />
          <Button onClick={thisYear}>
            {t('ThisYear')}
          </Button>
        </div>
      </section>

      {report.isPending ? (
        <Skeleton height={320} />
      ) : report.isError && !report.data ? (
        <div className={styles.error} role="alert">
          <span>{report.error.message}</span>
          <Button onClick={() => void report.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      ) : report.data ? (
        <Results report={report.data} refreshing={report.isFetching} />
      ) : null}
    </div>
  )
}
