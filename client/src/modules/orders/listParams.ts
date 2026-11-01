import { ORDER_STATUSES, type OrderListParams } from './api'

export const SORT_KEYS = ['date', 'number', 'customer', 'total', 'status', 'items'] as const
export const PAGE_SIZES = [10, 25, 50, 100]
export const DEFAULT_PAGE_SIZE = 25

const isDay = (v: string | null): v is string => v !== null && /^\d{4}-\d{2}-\d{2}$/.test(v)

/** The list's state lives in the URL (back button, reload and shared links keep the filters). Unknown values fall back to defaults. */
export function readListParams(search: URLSearchParams): OrderListParams {
  const page = Number(search.get('page'))
  const size = Number(search.get('size'))
  const customer = Number(search.get('customer'))
  const sortParam = search.get('sort')
  const sort = SORT_KEYS.includes(sortParam as (typeof SORT_KEYS)[number]) ? (sortParam as string) : 'date'
  const status = search.get('status')
  return {
    q: search.get('q') ?? '',
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE,
    sort,
    dir: search.get('dir') === 'asc' ? 'asc' : search.get('dir') === 'desc' ? 'desc' : sort !== 'date' ? 'asc' : 'desc',
    status: ORDER_STATUSES.some((s) => String(s) === status) ? (status as string) : '',
    from: isDay(search.get('from')) ? (search.get('from') as string) : '',
    to: isDay(search.get('to')) ? (search.get('to') as string) : '',
    customerId: Number.isInteger(customer) && customer > 0 ? customer : null,
  }
}

/** Only what differs from the defaults goes into the URL. */
export function writeListParams(p: OrderListParams): URLSearchParams {
  const out = new URLSearchParams()
  if (p.q) out.set('q', p.q)
  if (p.page > 1) out.set('page', String(p.page))
  if (p.pageSize !== DEFAULT_PAGE_SIZE) out.set('size', String(p.pageSize))
  if (p.sort !== 'date' || p.dir !== 'desc') {
    out.set('sort', p.sort)
    out.set('dir', p.dir)
  }
  if (p.status) out.set('status', p.status)
  if (p.from) out.set('from', p.from)
  if (p.to) out.set('to', p.to)
  if (p.customerId) out.set('customer', String(p.customerId))
  return out
}
