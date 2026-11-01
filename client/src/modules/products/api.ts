import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { http, type Download } from '@/api/http'
import type { components } from '@/api/schema'

type Schemas = components['schemas']

export type ProductListItem = Schemas['ProductListItemDto']
export type ProductDetail = Schemas['ProductDetailDto']
export type ProductSummary = Schemas['ProductListSummaryDto']
export type ProductLookups = Schemas['ProductLookupsDto']
export type ProductSaveRequest = Schemas['ProductSaveRequest']
export type ProductPriceTier = Schemas['ProductPriceTierDto']
export type ProductImportResult = Schemas['ProductImportResultDto']
export type ProductOption = Schemas['ProductOptionDto']
export type StickerSettings = Schemas['StickerSettingsDto']

export type StockLevel = 'out' | 'low' | 'ok'
export type StatusFilter = 'all' | 'active' | 'inactive'
export type StockFilter = 'all' | 'low' | 'out'

/** The URL-mirrored state of the list. */
export interface ProductListParams {
  q: string
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  categoryId: number | null
  status: StatusFilter
  stock: StockFilter
}

export const productKeys = {
  all: ['products'] as const,
  list: (params: ProductListParams) => ['products', 'list', params] as const,
  summary: ['products', 'summary'] as const,
  lookups: ['products', 'lookups'] as const,
  detail: (id: number) => ['products', 'detail', id] as const,
  stickerSettings: ['products', 'sticker-settings'] as const,
}

export const productsApi = {
  list: (params: ProductListParams, signal?: AbortSignal) =>
    http.get<Schemas['ProductListItemDtoPagedResult']>('/products', {
      signal,
      query: {
        q: params.q,
        page: params.page,
        pageSize: params.pageSize,
        sort: params.sort,
        dir: params.dir,
        categoryId: params.categoryId,
        status: params.status === 'all' ? null : params.status,
        stock: params.stock === 'all' ? null : params.stock,
      },
    }),
  summary: (signal?: AbortSignal) => http.get<ProductSummary>('/products/summary', { signal }),
  lookups: (signal?: AbortSignal) => http.get<ProductLookups>('/products/lookups', { signal }),
  get: (id: number, signal?: AbortSignal) => http.get<ProductDetail>(`/products/${id}`, { signal }),
  create: (body: ProductSaveRequest) => http.post<ProductDetail>('/products', body),
  update: (id: number, body: ProductSaveRequest) => http.put<ProductDetail>(`/products/${id}`, body),
  remove: (id: number) => http.delete<void>(`/products/${id}`),
  options: (q: string, signal?: AbortSignal) => http.get<ProductOption[]>('/products/options', { query: { q }, signal }),
  generateBarcode: () => http.get<{ barcode: string }>('/products/generate-barcode'),
  stickerSettings: (signal?: AbortSignal) => http.get<StickerSettings>('/products/sticker-settings', { signal }),
  export: () => http.download('/products/export'),
  importTemplate: () => http.download('/products/import-template'),
  import: (file: File, apply: boolean) => {
    const form = new FormData()
    form.set('file', file)
    form.set('apply', apply ? 'true' : 'false')
    return http.post<ProductImportResult>('/products/import', form)
  },
}

export function useProductList(params: ProductListParams) {
  return useQuery({
    queryKey: productKeys.list(params),
    queryFn: ({ signal }) => productsApi.list(params, signal),
    placeholderData: keepPreviousData,
  })
}

export function useProductSummary() {
  return useQuery({ queryKey: productKeys.summary, queryFn: ({ signal }) => productsApi.summary(signal) })
}

/** Categories / suppliers / currencies change rarely: keep them for the session's pages. */
export function useProductLookups() {
  return useQuery({ queryKey: productKeys.lookups, queryFn: ({ signal }) => productsApi.lookups(signal), staleTime: 5 * 60_000 })
}

export function useProduct(id: number | null) {
  return useQuery({
    queryKey: productKeys.detail(id ?? 0),
    queryFn: ({ signal }) => productsApi.get(id as number, signal),
    enabled: id !== null,
  })
}

export function useStickerSettings() {
  return useQuery({ queryKey: productKeys.stickerSettings, queryFn: ({ signal }) => productsApi.stickerSettings(signal), staleTime: 60_000 })
}

/** Any change to a product makes every list, tile and detail stale. */
export function useInvalidateProducts() {
  const client = useQueryClient()
  return () => client.invalidateQueries({ queryKey: productKeys.all })
}

export function useSaveProduct(id: number | null) {
  const invalidate = useInvalidateProducts()
  return useMutation({
    mutationFn: (body: ProductSaveRequest) => (id === null ? productsApi.create(body) : productsApi.update(id, body)),
    onSuccess: () => invalidate(),
  })
}

export function useDeleteProduct() {
  const invalidate = useInvalidateProducts()
  return useMutation({ mutationFn: (id: number) => productsApi.remove(id), onSuccess: () => invalidate() })
}

/** Hands a generated file to the browser's download bar. */
export function saveDownload(file: Download, fallbackName: string) {
  const url = URL.createObjectURL(file.blob)
  const link = document.createElement('a')
  link.href = url
  link.download = file.filename || fallbackName
  link.style.display = 'none'
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

/** The product's stock colour, from the level the server decided (never recomputed in the browser). */
export function isStockLevel(value: string): value is StockLevel {
  return value === 'out' || value === 'low' || value === 'ok'
}
