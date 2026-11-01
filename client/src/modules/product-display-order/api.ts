import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'

type Schemas = components['schemas']
export type DisplayOrder = Schemas['DisplayOrderDto']
export type DisplayOrderItem = Schemas['DisplayOrderItemDto']
export type AvailableProducts = Schemas['AvailableProductsDto']
export type AvailableProduct = Schemas['AvailableProductDto']
export type QuickAddOptions = Schemas['QuickAddOptionsDto']
export type QuickAddInput = Schemas['QuickAddProductRequest']
export type QuickAddResult = Schemas['QuickAddResultDto']

const BASE = '/product-display-order'
const root = ['product-display-order'] as const
export const displayOrderKeys = {
  all: root,
  list: [...root, 'list'] as const,
  available: (q: string) => [...root, 'available', q] as const,
  quickAddOptions: [...root, 'quick-add-options'] as const,
}

/** What the picker shows while nothing is typed (a short preview), and per page while searching. */
export const PREVIEW_SIZE = 5
export const SEARCH_PAGE_SIZE = 20

export function useDisplayOrder() {
  return useQuery({ queryKey: displayOrderKeys.list, queryFn: ({ signal }) => http.get<DisplayOrder>(BASE, { signal }) })
}

/** Saves the order. The server numbers the rows 1..n in the order of `ids`. */
export function useSaveOrder() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (ids: number[]) => http.put<void>(`${BASE}/order`, { ids }),
    onSuccess: () => client.invalidateQueries({ queryKey: displayOrderKeys.all }),
  })
}

export function useAddToDisplayOrder() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (productId: number) => http.post<DisplayOrderItem>(`${BASE}/items`, { productId }),
    onSuccess: () => client.invalidateQueries({ queryKey: displayOrderKeys.all }),
  })
}

export function useRemoveFromDisplayOrder() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => http.delete<void>(`${BASE}/items/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: displayOrderKeys.all }),
  })
}

/** Products not on the main screen: a 5-row preview when `q` is empty, 20-row pages (load more) while searching. */
export function useAvailableProducts(q: string) {
  return useInfiniteQuery({
    queryKey: displayOrderKeys.available(q),
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) =>
      http.get<AvailableProducts>(`${BASE}/available-products`, { query: { q, skip: pageParam, take: q ? SEARCH_PAGE_SIZE : PREVIEW_SIZE }, signal }),
    getNextPageParam: (last, all) => (q && last.hasMore ? all.reduce((n, page) => n + page.items.length, 0) : undefined),
  })
}

export function useQuickAddOptions() {
  return useQuery({
    queryKey: displayOrderKeys.quickAddOptions,
    queryFn: ({ signal }) => http.get<QuickAddOptions>(`${BASE}/quick-add-options`, { signal }),
    staleTime: 5 * 60_000,
  })
}

export function useQuickAdd() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: QuickAddInput) => http.post<QuickAddResult>(`${BASE}/quick-add`, input),
    onSuccess: () => client.invalidateQueries({ queryKey: displayOrderKeys.all }),
  })
}
