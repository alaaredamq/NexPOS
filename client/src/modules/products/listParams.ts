import type { ProductListParams, StatusFilter, StockFilter } from './api'

export const SORT_KEYS = ['name', 'barcode', 'category', 'purchasePrice', 'sellingPrice', 'stock', 'status'] as const
export const PAGE_SIZES = [10, 25, 50, 100]
export const DEFAULT_PAGE_SIZE = 25

const oneOf = <T extends string>(value: string | null, allowed: readonly T[], fallback: T): T =>
  allowed.includes(value as T) ? (value as T) : fallback

const positiveInt = (value: string | null, fallback: number): number => {
  const n = Number(value)
  return Number.isInteger(n) && n > 0 ? n : fallback
}

/** The list's state lives in the URL (back button, reload and shared links keep the filters). Unknown values fall back to defaults. */
export function readListParams(search: URLSearchParams): ProductListParams {
  const category = Number(search.get('category'))
  return {
    q: search.get('q') ?? '',
    page: positiveInt(search.get('page'), 1),
    pageSize: PAGE_SIZES.includes(Number(search.get('size'))) ? Number(search.get('size')) : DEFAULT_PAGE_SIZE,
    sort: oneOf(search.get('sort'), SORT_KEYS, 'name'),
    dir: oneOf(search.get('dir'), ['asc', 'desc'] as const, 'asc'),
    categoryId: Number.isInteger(category) && category > 0 ? category : null,
    status: oneOf<StatusFilter>(search.get('status'), ['all', 'active', 'inactive'], 'all'),
    stock: oneOf<StockFilter>(search.get('stock'), ['all', 'low', 'out'], 'all'),
  }
}

/** Only what differs from the defaults goes into the URL. */
export function writeListParams(p: ProductListParams): URLSearchParams {
  const out = new URLSearchParams()
  if (p.q) out.set('q', p.q)
  if (p.page > 1) out.set('page', String(p.page))
  if (p.pageSize !== DEFAULT_PAGE_SIZE) out.set('size', String(p.pageSize))
  if (p.sort !== 'name' || p.dir !== 'asc') {
    out.set('sort', p.sort)
    out.set('dir', p.dir)
  }
  if (p.categoryId) out.set('category', String(p.categoryId))
  if (p.status !== 'all') out.set('status', p.status)
  if (p.stock !== 'all') out.set('stock', p.stock)
  return out
}
