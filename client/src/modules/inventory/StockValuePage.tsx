import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatMoney, formatNumber, formatPercent, formatQty } from '@/ui/format'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { PrintButtons, PrintFailure, useReportPrinting } from './PrintButtons'
import { useStockValueReport, type StockValueItem, type StockValueReport } from './api'
import styles from './inventory.module.css'

const DEFAULT_SORT = { key: 'value', dir: 'desc' } as const

interface BarListProps {
  title: string
  rows: StockValueReport['byCategory']
  notation: string
  /** Label for the row the server sends with an empty name (products with no category). */
  blankLabel: string
  emptyLabel: string
}

/** A ranked list of horizontal bars: values side by side beat a pie for reading "which is biggest", and need no chart library. */
function BarList({ title, rows, notation, blankLabel, emptyLabel }: BarListProps) {
  const max = Math.max(0, ...rows.map((r) => r.value))
  return (
    <section className={styles.card} aria-label={title}>
      <h2 className={styles.cardTitle}>{title}</h2>
      {rows.length === 0 ? (
        <span className={styles.muted}>{emptyLabel}</span>
      ) : (
        <ul className={styles.bars}>
          {rows.map((row, index) => (
            <li key={`${row.label}-${index}`} className={styles.bar}>
              <span className={styles.barLabel} title={row.label || blankLabel}>
                <bdi>{row.label || blankLabel}</bdi>
              </span>
              <span className={styles.barTrack} aria-hidden="true">
                <span className={styles.barFill} style={{ inlineSize: `${max > 0 ? Math.max(2, (row.value / max) * 100) : 0}%` }} />
              </span>
              <span className={`${styles.barValue} tabular-nums`}>{formatMoney(row.value, notation)}</span>
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

/** `/app/inventory/stock-value` - what the active stock is worth at cost, by category and by product. */
export default function StockValuePage() {
  const { t } = useTranslation(['inventory', 'common'])
  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const report = useStockValueReport(params)
  const printing = useReportPrinting('stock-value')

  const data = report.data
  const columns: Column<StockValueItem>[] = [
    { key: 'name', header: t('Product'), sortable: true, required: true, cell: (p) => <strong className={styles.name}><bdi>{p.name}</bdi></strong> },
    {
      key: 'category',
      header: t('Category'),
      sortable: true,
      hideBelow: 900,
      cell: (p) => (p.categoryName ? <bdi>{p.categoryName}</bdi> : <span className={styles.muted}>{t('NoCategory')}</span>),
    },
    {
      key: 'stock',
      header: t('StockQuantity'),
      sortable: true,
      align: 'center',
      cell: (p) => (
        <span className={styles.stock}>
          <bdi>{formatQty(p.stockQuantity)}</bdi>
          {p.unit && <span className={styles.unit}>{p.unit}</span>}
        </span>
      ),
    },
    { key: 'cost', header: t('CostPrice'), sortable: true, align: 'end', hideBelow: 700, cell: (p) => formatMoney(p.unitCost, p.currencyNotation) },
    { key: 'value', header: t('StockValue'), sortable: true, align: 'end', cell: (p) => <strong>{formatMoney(p.stockValue, p.currencyNotation)}</strong> },
    {
      key: 'share',
      header: t('ValuePercentage'),
      hideBelow: 900,
      cell: (p) => (
        <span className={styles.share}>
          <span className={styles.barTrack} aria-hidden="true">
            <span className={styles.barFill} style={{ inlineSize: `${Math.min(100, Math.max(0, p.valuePercent))}%` }} />
          </span>
          <span className={`${styles.shareText} tabular-nums`}>{formatPercent(p.valuePercent)}</span>
        </span>
      ),
    },
  ]

  return (
    <div className={styles.page}>
      <PageHeader title={t('StockValueReport')} count={data ? formatNumber(data.items.total, 0) : undefined}>
        <PrintButtons printing={printing} />
        <Link className={styles.link} to="/inventory">
          {t('BackToInventory')}
        </Link>
      </PageHeader>
      <PrintFailure printing={printing} />

      <div className={styles.tiles}>
        <StatTile label={t('TotalStockValue')} value={data ? formatMoney(data.totalValue, data.currencyNotation) : '-'} tone="success" sub={t('AtCostPrice')} />
        <StatTile label={t('TotalProducts')} value={data ? formatNumber(data.totalProducts, 0) : '-'} />
        <StatTile label={t('AvgStockValue')} value={data ? formatMoney(data.averageValue, data.currencyNotation) : '-'} />
        <StatTile label={t('TotalUnits')} value={data ? formatQty(data.totalUnits) : '-'} />
      </div>

      {data && (
        <div className={styles.charts}>
          <BarList title={t('StockValueByCategory')} rows={data.byCategory} notation={data.currencyNotation} blankLabel={t('NoCategory')} emptyLabel={t('NoStockValueDataAvailable')} />
          <BarList title={t('Top10ProductsByValue')} rows={data.topProducts} notation={data.currencyNotation} blankLabel={t('NoCategory')} emptyLabel={t('NoStockValueDataAvailable')} />
        </div>
      )}

      <DataTable<StockValueItem>
        caption={t('StockValueDetails')}
        columns={columns}
        rows={data?.items.items ?? []}
        rowKey={(p) => p.id}
        loading={report.isPending}
        refreshing={report.isFetching && !report.isPending}
        error={report.isError ? report.error.message : null}
        onRetry={() => void report.refetch()}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
        emptyTitle={params.q ? t('NoProductsMatch') : t('NoStockValueDataAvailable')}
        toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchProducts')} aria-label={t('SearchProducts')} />}
        footer={
          <Pagination
            page={params.page}
            pageSize={params.pageSize}
            total={data?.items.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={(page) => update({ page })}
            onPageSizeChange={(pageSize) => update({ pageSize })}
          />
        }
      />
    </div>
  )
}
