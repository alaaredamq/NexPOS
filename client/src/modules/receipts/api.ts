import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from '@/api/http'
import type { components } from '@/api/schema'

type Schemas = components['schemas']

export type ReceiptListItem = Schemas['ReceiptListItemDto']
export type ReceiptStats = Schemas['ReceiptStatsDto']
export type ReceiptCurrencyStats = Schemas['ReceiptCurrencyStatsDto']
export type ReceiptDetail = Schemas['ReceiptDetailDto']
export type SaleReversal = Schemas['SaleReversalDto']

export type StatusFilter = 'all' | 'valid' | 'void'

/** The URL-mirrored state of the list. Dates are local days (yyyy-mm-dd) or ''. */
export interface ReceiptListParams {
  q: string
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  status: StatusFilter
  from: string
  to: string
}

export const receiptKeys = {
  all: ['receipts'] as const,
  list: (params: ReceiptListParams) => ['receipts', 'list', params] as const,
  stats: (from: string, to: string) => ['receipts', 'stats', from, to] as const,
  detail: (id: number) => ['receipts', 'detail', id] as const,
}

export function useReceiptList(params: ReceiptListParams) {
  return useQuery({
    queryKey: receiptKeys.list(params),
    queryFn: ({ signal }) =>
      http.get<Schemas['ReceiptListItemDtoPagedResult']>('/receipts', {
        signal,
        query: {
          q: params.q,
          page: params.page,
          pageSize: params.pageSize,
          sort: params.sort,
          dir: params.dir,
          status: params.status === 'all' ? null : params.status,
          from: params.from,
          to: params.to,
        },
      }),
    placeholderData: keepPreviousData,
  })
}

/** With no range the server answers for the last 30 days and says which days (`from` / `to`). */
export function useReceiptStats(from: string, to: string) {
  return useQuery({
    queryKey: receiptKeys.stats(from, to),
    queryFn: ({ signal }) => http.get<ReceiptStats>('/receipts/stats', { query: { from, to }, signal }),
    placeholderData: keepPreviousData,
  })
}

export function useReceipt(id: number | null) {
  return useQuery({
    queryKey: receiptKeys.detail(id ?? 0),
    queryFn: ({ signal }) => http.get<ReceiptDetail>(`/receipts/${id}`, { signal }),
    enabled: id !== null,
  })
}

/** Voiding reverses the sale: receipts, orders and stock all go stale. */
export function useVoidReceipt() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, reason }: { id: number; reason: string }) => http.post<SaleReversal>(`/receipts/${id}/void`, { reason }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: receiptKeys.all })
      void client.invalidateQueries({ queryKey: ['orders'] })
      void client.invalidateQueries({ queryKey: ['products'] })
    },
  })
}
