import { useTranslation } from 'react-i18next'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { Can } from '@/core/permissions/permissions'
import { ChipGroup } from '@/ui/ChipGroup'
import { formatDateTime, formatNumber, formatQty } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { StatTile } from '@/ui/StatTile'
import { useProductHistory, type HistoryKind, type ProductHistory } from './api'
import { describeEvent } from './historyText'
import { NotFoundCard } from './NotFoundCard'
import styles from './ProductHistory.module.css'

const KINDS: HistoryKind[] = ['all', 'stock', 'sales', 'changes']
const PAGE_SIZE = 25

const asKind = (value: string | null): HistoryKind => (KINDS.find((k) => k === value) ?? 'all')

/** `/products/:id/history`: everything that was done to one product, newest first - stock added or removed, sales, returns, price and detail changes. */
export default function ProductHistoryPage() {
  const { t } = useTranslation('products')
  const params = useParams()
  const id = Number(params.id)
  const validId = Number.isInteger(id) && id > 0

  const [search, setSearch] = useSearchParams()
  const kind = asKind(search.get('kind'))
  const pageParam = Number(search.get('page'))
  const page = Number.isInteger(pageParam) && pageParam >= 1 ? pageParam : 1

  const history = useProductHistory(validId ? id : null, { kind, page, pageSize: PAGE_SIZE })

  const show = (next: { kind?: HistoryKind; page?: number }) => {
    const merged = { kind, page: 1, ...next }
    const query = new URLSearchParams()
    if (merged.kind !== 'all') query.set('kind', merged.kind)
    if (merged.page > 1) query.set('page', String(merged.page))
    setSearch(query, { replace: true })
  }

  if (!validId || (history.error && !history.data)) return <NotFoundCard error={history.error} />
  if (!history.data) return <Loader fullSection size="lg" />
  const data = history.data

  return (
    <div className={styles.page}>
      <PageHeader title={t('history.title', { name: data.productName })} back={{ to: `/products/${id}`, label: t('history.back') }} />

      <div className={styles.tiles}>
        <StatTile label={t('history.tile.stock')} value={withUnit(data, data.currentStock, t)} />
        <StatTile label={t('history.tile.sold')} value={withUnit(data, data.totalSold, t)} tone="info" />
        <StatTile label={t('history.tile.added')} value={withUnit(data, data.totalAdded, t)} tone="success" />
        <StatTile label={t('history.tile.returned')} value={withUnit(data, data.totalReturned, t)} />
      </div>

      <section className={styles.card} aria-label={t('history.button')}>
        <div className={styles.toolbar}>
          <ChipGroup
            aria-label={t('history.filter.label')}
            value={kind}
            onChange={(value) => show({ kind: asKind(value) })}
            options={KINDS.map((k) => ({ value: k, label: t(`history.filter.${k}`) }))}
          />
          <span className={styles.count}>{formatNumber(data.items.total, 0)}</span>
        </div>

        {data.items.items.length === 0 ? (
          <div className={styles.empty}>
            <strong>{kind === 'all' ? t('history.empty') : t('history.emptyFiltered')}</strong>
            <span>{t('history.emptyHint')}</span>
          </div>
        ) : (
          <ol className={styles.list} data-refreshing={history.isFetching || undefined}>
            {data.items.items.map((item) => {
              const event = describeEvent(item, { t, unit: data.unit, currencyNotation: data.currencyNotation })
              return (
                <li key={item.id} className={styles.row} data-tone={event.tone}>
                  <span className={styles.dot} aria-hidden="true" />
                  <div className={styles.body}>
                    <div className={styles.text}>{event.text}</div>
                    {event.stock && (
                      <div className={`${styles.meta} tabular-nums`}>
                        {t('history.stockLine', { before: formatQty(event.stock.before), after: formatQty(event.stock.after) })}
                      </div>
                    )}
                    {item.reason && <div className={styles.meta}>{t('history.reason', { reason: item.reason })}</div>}
                    <div className={styles.meta}>
                      <time dateTime={item.createdAt}>{formatDateTime(item.createdAt)}</time>
                      {item.userName && <span>{t('history.by', { user: item.userName })}</span>}
                      {item.orderId != null && (
                        <Can permission={Permissions.Orders_View}>
                          <Link to={`/orders/${item.orderId}`}>{item.orderNumber}</Link>
                        </Can>
                      )}
                    </div>
                  </div>
                </li>
              )
            })}
          </ol>
        )}

        <Pagination page={page} pageSize={PAGE_SIZE} total={data.items.total} pageSizes={[PAGE_SIZE]} onPageChange={(next) => show({ page: next })} />
      </section>
    </div>
  )
}

/** "12 units" for pieces, "2.5 kg" for weighed products. */
function withUnit(data: ProductHistory, value: number, t: (key: string, options?: Record<string, unknown>) => string): string {
  const n = formatQty(value)
  return data.unit && data.unit !== 'piece' ? `${n} ${data.unit}` : t('history.units', { count: n })
}
