import { useEffect, useMemo, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { useVirtualizer } from '@tanstack/react-virtual'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { usePosProducts, type PosProduct } from './api'
import { ProductTile } from './ProductTile'
import { useGridColumns } from './useGridColumns'
import styles from './catalog.module.css'

/** Tile geometry. The row height is fixed (a tile is two lines of name plus a footer), which lets the virtualizer skip measuring. */
const TILE_MIN_WIDTH = 132
const TILE_HEIGHT = 80
const GAP = 8
const FETCH_AHEAD_ROWS = 3

interface ProductGridProps {
  /** `null` is the main screen. */
  categoryId: number | null
  onPress: (product: PosProduct) => void
  /** Told whether the main-screen list is the shop's display order or just every product, so the first chip can say which. */
  onMainScreenKind?: (isMainScreen: boolean) => void
}

export function ProductGrid({ categoryId, onPress, onMainScreenKind }: ProductGridProps) {
  const { t } = useTranslation(['pos', 'common'])
  const scrollRef = useRef<HTMLDivElement>(null)
  const query = usePosProducts(categoryId)
  const columns = useGridColumns(scrollRef, TILE_MIN_WIDTH, GAP)

  const products = useMemo(() => query.data?.pages.flatMap((page) => page.items) ?? [], [query.data])
  const total = query.data?.pages[0]?.total ?? 0
  const isMainScreen = query.data?.pages[0]?.isMainScreen

  useEffect(() => {
    if (categoryId === null && isMainScreen !== undefined) onMainScreenKind?.(isMainScreen)
  }, [categoryId, isMainScreen, onMainScreenKind])

  const rowCount = Math.ceil(products.length / columns)
  // TanStack Virtual hands out non-memoizable functions; nothing here is passed to a memoized child.
  // oxlint-disable-next-line react/incompatible-library
  const virtualizer = useVirtualizer({
    count: rowCount,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => TILE_HEIGHT + GAP,
    overscan: 4,
  })
  const rows = virtualizer.getVirtualItems()
  const lastRow = rows.at(-1)?.index ?? -1

  // Page in the next batch a few rows before the cashier reaches the end.
  const { hasNextPage, isFetchingNextPage, fetchNextPage } = query
  useEffect(() => {
    if (hasNextPage && !isFetchingNextPage && lastRow >= rowCount - FETCH_AHEAD_ROWS) void fetchNextPage()
  }, [hasNextPage, isFetchingNextPage, fetchNextPage, lastRow, rowCount])

  // A new category starts at the top.
  useEffect(() => {
    scrollRef.current?.scrollTo?.({ top: 0 })
  }, [categoryId])

  let state: 'loading' | 'error' | 'empty' | 'ready' = 'ready'
  if (query.isPending) state = 'loading'
  else if (query.isError && products.length === 0) state = 'error'
  else if (products.length === 0) state = 'empty'

  return (
    <div className={styles.gridWrap}>
      <div ref={scrollRef} className={styles.gridScroll} data-testid="product-grid" aria-busy={query.isFetching && !query.isPending}>
        {state === 'ready' && (
          <ul className={styles.gridCanvas} style={{ height: virtualizer.getTotalSize() }} aria-label={t('ProductCatalog', { ns: 'pos' })}>
            {rows.map((row) => (
              <li
                key={row.key}
                className={styles.gridRow}
                style={{ height: TILE_HEIGHT, transform: `translateY(${row.start}px)`, gridTemplateColumns: `repeat(${columns}, minmax(0, 1fr))` }}
              >
                {products.slice(row.index * columns, row.index * columns + columns).map((product) => (
                  <ProductTile key={product.id} product={product} onPress={onPress} />
                ))}
              </li>
            ))}
          </ul>
        )}
        {state === 'loading' && <Loader fullSection size="lg" />}
        {state === 'empty' && <p className={styles.gridMessage}>{t('NoProductsFound', { ns: 'pos' })}</p>}
        {state === 'error' && (
          <div className={styles.gridMessage} role="alert">
            <p>{isApiError(query.error) ? query.error.message : t('CouldNotLoadProducts', { ns: 'pos' })}</p>
            <Button onClick={() => void query.refetch()}>{t('priceRetry', { ns: 'pos' })}</Button>
          </div>
        )}
      </div>
      {state === 'ready' && total > 0 && (
        <div className={styles.gridCount} aria-live="polite">
          {t('ShowingProducts', { ns: 'pos', shown: products.length, total })}
        </div>
      )}
    </div>
  )
}
