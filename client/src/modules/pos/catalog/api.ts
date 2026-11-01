import { keepPreviousData, useInfiniteQuery, useQuery } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { ApiError, http } from '@/api/http'

type Schemas = components['schemas']
export type PosProduct = Schemas['PosProductDto']
export type PosCategory = Schemas['PosCategoryDto']
export type PosProductPage = Schemas['PosProductPageDto']
export type PosSearchResult = Schemas['PosSearchResultDto']

/** Tiles fetched per request; the grid is virtualised, so this only bounds the payload, not the DOM. */
export const PRODUCT_PAGE_SIZE = 120
/** The server returns name matches 20 at a time (`skip` = items received so far). */
export const SEARCH_PAGE_SIZE = 20

/**
 * The till shows stock and prices, which other screens change (an inventory adjustment, a product edit, another till's sale). The app-wide
 * 30 s freshness would keep showing the old number after coming back from Inventory, so every visit to the till asks again; the cached tiles
 * stay on screen meanwhile (placeholder) so nothing flashes.
 */
const ALWAYS_FRESH = { staleTime: 0, refetchOnMount: 'always' as const, refetchOnWindowFocus: 'always' as const }

const root = ['pos', 'catalog'] as const
export const catalogKeys = {
  categories: [...root, 'categories'] as const,
  products: (categoryId: number | null) => [...root, 'products', categoryId ?? 'main'] as const,
  search: (term: string) => [...root, 'search', term] as const,
}

export function usePosCategories() {
  return useQuery({
    queryKey: catalogKeys.categories,
    queryFn: ({ signal }) => http.get<PosCategory[]>('/pos/categories', { signal }),
    ...ALWAYS_FRESH,
  })
}

/**
 * The product grid, a page at a time. `categoryId: null` is the main screen (the shop's display order, or every active product when none
 * is configured: `isMainScreen` on the page says which). Kept fresh by the standard stale time and window-focus refetch, so stock numbers
 * on the tiles follow sales made on another till.
 */
export function usePosProducts(categoryId: number | null) {
  return useInfiniteQuery({
    queryKey: catalogKeys.products(categoryId),
    initialPageParam: 1,
    queryFn: ({ pageParam, signal }) =>
      http.get<PosProductPage>('/pos/products', {
        query: { page: pageParam, pageSize: PRODUCT_PAGE_SIZE, ...(categoryId === null ? { mainScreen: true } : { categoryId }) },
        signal,
      }),
    getNextPageParam: (last) => (last.page * last.pageSize < last.total ? last.page + 1 : undefined),
    placeholderData: keepPreviousData,
    ...ALWAYS_FRESH,
  })
}

/** Name / barcode search for the search box: pages of 20, `skip` = how many the box already shows. */
export function usePosSearch(term: string) {
  return useInfiniteQuery({
    queryKey: catalogKeys.search(term),
    enabled: term.length > 0,
    initialPageParam: 0,
    queryFn: ({ pageParam, signal }) => http.get<PosSearchResult>('/pos/products/search', { query: { q: term, skip: pageParam }, signal }),
    getNextPageParam: (last, pages) => (last.hasMore ? pages.reduce((n, p) => n + p.items.length, 0) : undefined),
    placeholderData: keepPreviousData,
    ...ALWAYS_FRESH,
  })
}

/**
 * Scanner lookup: an exact barcode, never a name search. Rejects with an `ApiError` whose code is `product_not_found` for an unknown code.
 * Not cached: stock must be what the server says right now.
 */
export async function lookupBarcode(code: string, signal?: AbortSignal): Promise<PosProduct> {
  const result = await http.get<PosSearchResult>('/pos/products/search', { query: { q: code, barcodeOnly: true }, signal })
  const product = result.items[0]
  if (!product) throw new ApiError(404, { code: 'product_not_found' }, 'Product not found.')
  return product
}
