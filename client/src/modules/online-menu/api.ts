import { useInfiniteQuery, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'

type Schemas = components['schemas']
export type OnlineMenu = Schemas['OnlineMenuDto']
export type OnlineMenuSection = Schemas['OnlineMenuSectionDto']
export type OnlineMenuEntry = Schemas['OnlineMenuEntryDto']
export type OnlineMenuEntries = Schemas['OnlineMenuEntriesDto']
export type OnlineMenuStatus = Schemas['OnlineMenuStatusDto']
export type ProductOption = Schemas['OnlineMenuProductOptionDto']
export type ProductOptions = Schemas['OnlineMenuProductOptionsDto']
export type CategoryOption = Schemas['OnlineMenuCategoryOptionDto']

const BASE = '/online-menu'
const root = ['online-menu'] as const
export const onlineMenuKeys = {
  all: root,
  menu: [...root, 'menu'] as const,
  entries: (sectionId: number) => [...root, 'entries', sectionId] as const,
  products: (q: string) => [...root, 'products', q] as const,
  categories: [...root, 'categories'] as const,
}

export const PRODUCT_PAGE = 30
/** Picks per page in a section: a section can hold thousands of products, so they load as you scroll. */
export const ENTRY_PAGE = 30

/** Refreshes every few seconds while the cloud has not answered yet, so "Checking..." turns into an answer by itself. */
export function useOnlineMenu() {
  return useQuery({
    queryKey: onlineMenuKeys.menu,
    queryFn: ({ signal }) => http.get<OnlineMenu>(BASE, { signal }),
    refetchInterval: (query) => {
      const status = query.state.data?.status
      return status?.connected && (status.linked == null || status.lastSentUtc == null) ? 5000 : 30_000
    },
  })
}

function useMenuMutation<TVars, TResult = unknown>(fn: (vars: TVars) => Promise<TResult>) {
  const client = useQueryClient()
  return useMutation({ mutationFn: fn, onSuccess: () => client.invalidateQueries({ queryKey: onlineMenuKeys.all }) })
}

export const useAddSection = () => useMenuMutation((name: string) => http.post<OnlineMenuSection>(`${BASE}/sections`, { name }))
export const useRenameSection = () =>
  useMenuMutation(({ id, name }: { id: number; name: string }) => http.put<void>(`${BASE}/sections/${id}`, { name }))
export const useDeleteSection = () => useMenuMutation((id: number) => http.delete<void>(`${BASE}/sections/${id}`))
export const useReorderSections = () => useMenuMutation((ids: number[]) => http.put<void>(`${BASE}/sections/order`, { ids }))
export const usePlace = () =>
  useMenuMutation(({ sectionId, productIds, categoryIds }: { sectionId: number; productIds: number[]; categoryIds: number[] }) =>
    http.post<void>(`${BASE}/sections/${sectionId}/entries`, { productIds, categoryIds }),
  )
export const useRemoveEntry = () => useMenuMutation((id: number) => http.delete<void>(`${BASE}/entries/${id}`))
/** One step up (-1) or down (1). The server swaps it with its neighbour, so this works while a long section is only partly loaded. */
export const useMoveEntry = () =>
  useMenuMutation(({ id, direction }: { id: number; direction: -1 | 1 }) => http.post<void>(`${BASE}/entries/${id}/move`, { direction }))

export function useSectionEntries(sectionId: number) {
  return useInfiniteQuery({
    queryKey: onlineMenuKeys.entries(sectionId),
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) =>
      http.get<OnlineMenuEntries>(`${BASE}/sections/${sectionId}/entries`, { query: { skip: pageParam, take: ENTRY_PAGE }, signal }),
    getNextPageParam: (last, all) => (last.hasMore ? all.reduce((n, page) => n + page.items.length, 0) : undefined),
  })
}
export const useSendNow = () => useMenuMutation(() => http.post<void>(`${BASE}/send-now`))

export function useProductOptions(q: string) {
  return useInfiniteQuery({
    queryKey: onlineMenuKeys.products(q),
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => http.get<ProductOptions>(`${BASE}/products`, { query: { q, skip: pageParam, take: PRODUCT_PAGE }, signal }),
    getNextPageParam: (last, all) => (last.hasMore ? all.reduce((n, page) => n + page.items.length, 0) : undefined),
  })
}

export function useCategoryOptions() {
  return useQuery({ queryKey: onlineMenuKeys.categories, queryFn: ({ signal }) => http.get<CategoryOption[]>(`${BASE}/categories`, { signal }) })
}

/** `ids` with the item at `from` moved one step up (-1) or down (+1). */
export function moved(ids: number[], from: number, step: -1 | 1): number[] {
  const to = from + step
  if (from < 0 || to < 0 || to >= ids.length) return ids
  const next = [...ids]
  next[from] = ids[to]!
  next[to] = ids[from]!
  return next
}
