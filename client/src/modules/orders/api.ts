import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http } from '@/api/http'
import type { components } from '@/api/schema'

type Schemas = components['schemas']

export type OrderListItem = Schemas['OrderListItemDto']
export type OrderSummary = Schemas['OrderSummaryDto']
export type OrderDetail = Schemas['OrderDetailDto']
export type OrderLine = Schemas['OrderLineDto']
export type OrderStatusResult = Schemas['OrderStatusResultDto']

/** 0 Pending, 1 Processing, 2 Shipped, 3 Delivered, 4 Cancelled, 5 Returned (the server's enum). */
export const ORDER_STATUSES = [0, 1, 2, 3, 4, 5] as const
export type OrderStatus = (typeof ORDER_STATUSES)[number]
export const STATUS_KEYS: Record<number, string> = { 0: 'Pending', 1: 'Processing', 2: 'Shipped', 3: 'Delivered', 4: 'Cancelled', 5: 'Returned' }
export const STATUS_TONES: Record<number, 'warning' | 'info' | 'ink' | 'success' | 'danger' | 'neutral'> = {
  0: 'warning',
  1: 'info',
  2: 'ink',
  3: 'success',
  4: 'danger',
  5: 'neutral',
}

/** The URL-mirrored state of the list. `status` is '' for every status. Dates are local days (yyyy-mm-dd) or ''. */
export interface OrderListParams {
  q: string
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  status: string
  from: string
  to: string
  customerId: number | null
}

export const orderKeys = {
  all: ['orders'] as const,
  list: (params: OrderListParams) => ['orders', 'list', params] as const,
  summary: ['orders', 'summary'] as const,
  detail: (id: number) => ['orders', 'detail', id] as const,
}

const query = (p: OrderListParams) => ({
  q: p.q,
  page: p.page,
  pageSize: p.pageSize,
  sort: p.sort,
  dir: p.dir,
  status: p.status,
  from: p.from,
  to: p.to,
  customerId: p.customerId,
})

export const ordersApi = {
  list: (params: OrderListParams, signal?: AbortSignal) => http.get<Schemas['OrderListItemDtoPagedResult']>('/orders', { query: query(params), signal }),
  /** Every page of the filtered list as a CSV. */
  export: (params: OrderListParams) => http.download('/orders/export', { query: { ...query(params), page: undefined, pageSize: undefined } }),
}

export function useOrderList(params: OrderListParams) {
  return useQuery({
    queryKey: orderKeys.list(params),
    queryFn: ({ signal }) => ordersApi.list(params, signal),
    placeholderData: keepPreviousData,
  })
}

export function useOrderSummary() {
  return useQuery({ queryKey: orderKeys.summary, queryFn: ({ signal }) => http.get<OrderSummary>('/orders/summary', { signal }) })
}

export function useOrder(id: number | null) {
  return useQuery({
    queryKey: orderKeys.detail(id ?? 0),
    queryFn: ({ signal }) => http.get<OrderDetail>(`/orders/${id}`, { signal }),
    enabled: id !== null,
  })
}

/** A status change - above all a cancellation - moves orders, receipts and stock, so every one of those goes stale. */
export function useUpdateOrderStatus() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, status }: { id: number; status: number }) => http.put<OrderStatusResult>(`/orders/${id}/status`, { status }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: orderKeys.all })
      void client.invalidateQueries({ queryKey: ['receipts'] })
      void client.invalidateQueries({ queryKey: ['products'] })
    },
  })
}
