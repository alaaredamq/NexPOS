import type { ReceiptListParams, StatusFilter } from './api'

export const SORT_KEYS = ['date', 'number', 'customer', 'cashier', 'amount', 'payment', 'voidDate'] as const
export const PAGE_SIZES = [10, 25, 50, 100]
export const DEFAULT_PAGE_SIZE = 25
export type View = 'list' | 'analytics'

const isDay = (v: string | null): v is string => v !== null && /^\d{4}-\d{2}-\d{2}$/.test(v)

/** The page state lives in the URL (back button, reload and shared links keep the filters). Unknown values fall back to defaults. */
export function readListParams(search: URLSearchParams): ReceiptListParams & { view: View } {
  const page = Number(search.get('page'))
  const size = Number(search.get('size'))
  const sortParam = search.get('sort')
  const sort = SORT_KEYS.includes(sortParam as (typeof SORT_KEYS)[number]) ? (sortParam as string) : 'date'
  const status = search.get('status')
  const dir = search.get('dir')
  return {
    q: search.get('q') ?? '',
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE,
    sort,
    dir: dir === 'asc' || dir === 'desc' ? dir : sort !== 'date' ? 'asc' : 'desc',
    status: status === 'valid' || status === 'void' ? (status as StatusFilter) : 'all',
    from: isDay(search.get('from')) ? (search.get('from') as string) : '',
    to: isDay(search.get('to')) ? (search.get('to') as string) : '',
    view: search.get('view') === 'analytics' ? 'analytics' : 'list',
  }
}

/** Only what differs from the defaults goes into the URL. */
export function writeListParams(p: ReceiptListParams & { view: View }): URLSearchParams {
  const out = new URLSearchParams()
  if (p.q) out.set('q', p.q)
  if (p.page > 1) out.set('page', String(p.page))
  if (p.pageSize !== DEFAULT_PAGE_SIZE) out.set('size', String(p.pageSize))
  if (p.sort !== 'date' || p.dir !== 'desc') {
    out.set('sort', p.sort)
    out.set('dir', p.dir)
  }
  if (p.status !== 'all') out.set('status', p.status)
  if (p.from) out.set('from', p.from)
  if (p.to) out.set('to', p.to)
  if (p.view !== 'list') out.set('view', p.view)
  return out
}
