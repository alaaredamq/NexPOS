import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from '@/api/http'
import type { components } from '@/api/schema'
import { saveDownload } from '../_shared/saveDownload'

type Schemas = components['schemas']

export type PurchaseListItem = Schemas['PurchaseListItemDto']
export type PurchaseList = Schemas['PurchaseListItemDtoPagedResult']
export type PurchaseSummary = Schemas['PurchaseSummaryDto']
export type PurchaseDetail = Schemas['PurchaseDetailDto']
export type PurchaseItem = Schemas['PurchaseItemDto']
export type PurchaseInput = Schemas['PurchaseRequest']
export type PurchaseLineInput = Schemas['PurchaseLineRequest']

export type PurchaseStatus = 'received' | 'cancelled'
export const PAGE_SIZES = [10, 25, 50, 100]
export const DEFAULT_PAGE_SIZE = 25
export const SORT_KEYS = ['date', 'number', 'supplier', 'reference', 'total', 'status'] as const

/** The URL-mirrored state of the list. Empty string = no filter. */
export interface PurchaseListParams {
  q: string
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  status: '' | PurchaseStatus
  from: string
  to: string
  supplierId: number | null
}

const isDay = (v: string | null): v is string => v !== null && /^\d{4}-\d{2}-\d{2}$/.test(v)

export function readListParams(search: URLSearchParams): PurchaseListParams {
  const page = Number(search.get('page'))
  const size = Number(search.get('size'))
  const supplier = Number(search.get('supplier'))
  const sortParam = search.get('sort')
  const sort = SORT_KEYS.includes(sortParam as (typeof SORT_KEYS)[number]) ? (sortParam as string) : 'date'
  const status = search.get('status')
  return {
    q: search.get('q') ?? '',
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE,
    sort,
    dir: search.get('dir') === 'asc' ? 'asc' : search.get('dir') === 'desc' ? 'desc' : sort !== 'date' ? 'asc' : 'desc',
    status: status === 'received' || status === 'cancelled' ? status : '',
    from: isDay(search.get('from')) ? (search.get('from') as string) : '',
    to: isDay(search.get('to')) ? (search.get('to') as string) : '',
    supplierId: Number.isInteger(supplier) && supplier > 0 ? supplier : null,
  }
}

/** Only what differs from the defaults goes into the URL. */
export function writeListParams(p: PurchaseListParams): URLSearchParams {
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
  if (p.supplierId) out.set('supplier', String(p.supplierId))
  return out
}

const filterQuery = (p: PurchaseListParams) => ({
  q: p.q || null,
  status: p.status || null,
  from: p.from || null,
  to: p.to || null,
  supplierId: p.supplierId,
})

const root = ['purchases'] as const
export const purchaseKeys = {
  all: root,
  list: (p: PurchaseListParams) => [...root, 'list', p] as const,
  summary: (p: PurchaseListParams) => [...root, 'summary', { ...filterQuery(p), status: null }] as const,
  detail: (id: number) => [...root, 'detail', id] as const,
}

export function usePurchaseList(params: PurchaseListParams, enabled = true) {
  return useQuery({
    queryKey: purchaseKeys.list(params),
    queryFn: ({ signal }) =>
      http.get<PurchaseList>('/purchases', {
        signal,
        query: { ...filterQuery(params), page: params.page, pageSize: params.pageSize, sort: params.sort, dir: params.dir },
      }),
    placeholderData: keepPreviousData,
    enabled,
  })
}

/** The tiles follow the supplier / date / search filters but not the status chip, so cancelled ones are always counted. */
export function usePurchaseSummary(params: PurchaseListParams) {
  return useQuery({
    queryKey: purchaseKeys.summary(params),
    queryFn: ({ signal }) => http.get<PurchaseSummary>('/purchases/summary', { signal, query: { ...filterQuery(params), status: null } }),
    placeholderData: keepPreviousData,
  })
}

export function usePurchase(id: number) {
  return useQuery({ queryKey: purchaseKeys.detail(id), queryFn: ({ signal }) => http.get<PurchaseDetail>(`/purchases/${id}`, { signal }) })
}

/**
 * A purchase moves stock, cost prices and a supplier balance, so everything that shows any of those refetches afterwards.
 * Never optimistic: it is stock and money.
 */
function useRefreshAfterChange() {
  const client = useQueryClient()
  return () => Promise.all(['purchases', 'suppliers', 'products', 'inventory', 'dashboard'].map((key) => client.invalidateQueries({ queryKey: [key] })))
}

export function useCreatePurchase() {
  const refresh = useRefreshAfterChange()
  return useMutation({
    mutationFn: (input: PurchaseInput) => http.post<PurchaseDetail>('/purchases', input),
    onSuccess: refresh,
  })
}

export function useCancelPurchase() {
  const refresh = useRefreshAfterChange()
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string | null }) => http.post<PurchaseDetail>(`/purchases/${id}/cancel`, { reason }),
    onSuccess: refresh,
  })
}

export async function downloadPurchasesCsv(params: PurchaseListParams): Promise<void> {
  saveDownload(await http.download('/purchases/export', { query: filterQuery(params) }), 'purchases.csv')
}

/** What a purchase adds up to, from the lines: the same arithmetic the server does, to preview before saving. */
export function purchaseTotals(lines: { quantity: number; unitCost: number }[], discountPercent: number) {
  const round = (n: number) => Math.round((n + Number.EPSILON) * 100) / 100
  const subtotal = round(lines.reduce((sum, l) => sum + round(l.quantity * l.unitCost), 0))
  const discount = round((subtotal * discountPercent) / 100)
  return { subtotal, discount, total: round(subtotal - discount) }
}
