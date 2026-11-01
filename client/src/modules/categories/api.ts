import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { ApiError, http } from '@/api/http'
import { getLanguage } from '@/core/i18n/languages'

type Schemas = components['schemas']
export type Category = Schemas['CategoryDto']
export type CategoryList = Schemas['CategoryDtoPagedResult']
export type CategorySummary = Schemas['CategorySummaryDto']
export type CategoryDetails = Schemas['CategoryDetailsDto']
export type CategoryProduct = Schemas['CategoryProductDto']
export type CategoryInput = Schemas['CategoryRequest']

export interface CategoryListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
}

const root = ['categories'] as const
export const categoryKeys = {
  all: root,
  list: (params: CategoryListParams) => [...root, 'list', params] as const,
  summary: [...root, 'summary'] as const,
  detail: (id: number) => [...root, 'detail', id] as const,
}

export function useCategories(params: CategoryListParams) {
  return useQuery({
    queryKey: categoryKeys.list(params),
    queryFn: ({ signal }) => http.get<CategoryList>('/categories', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

export function useCategorySummary() {
  return useQuery({ queryKey: categoryKeys.summary, queryFn: ({ signal }) => http.get<CategorySummary>('/categories/summary', { signal }) })
}

export function useCategory(id: number) {
  return useQuery({
    queryKey: categoryKeys.detail(id),
    queryFn: ({ signal }) => http.get<CategoryDetails>(`/categories/${id}`, { signal }),
    // A 404 is an answer ("deleted meanwhile"), not something to retry (the app-wide retry rule already skips 4xx).
  })
}

/** Create (no `id`) or update (with `id`): every list, tile and details page of categories is refetched afterwards. */
export function useSaveCategory() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: CategoryInput }) =>
      id === undefined ? http.post<Category>('/categories', input) : http.put<Category>(`/categories/${id}`, input),
    onSuccess: () => client.invalidateQueries({ queryKey: categoryKeys.all }),
  })
}

export function useDeleteCategory() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => http.delete<void>(`/categories/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: categoryKeys.all }),
  })
}

/** Downloads the CSV through the signed-in cookie. The API answers JSON problems for failures, so they surface as an `ApiError`. */
export async function downloadCategoriesCsv(): Promise<void> {
  const response = await fetch('/api/v1/categories/export', {
    credentials: 'same-origin',
    headers: { Accept: 'text/csv, application/problem+json', 'Accept-Language': getLanguage() },
  })
  if (!response.ok) {
    let problem: Partial<Schemas['ApiProblemDetails']> = {}
    try {
      problem = (await response.json()) as typeof problem
    } catch {
      // not JSON: keep the generic message
    }
    throw new ApiError(response.status, problem, `Export failed (${response.status}).`)
  }

  const disposition = response.headers.get('Content-Disposition') ?? ''
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? 'categories.csv'
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = decodeURIComponent(name)
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
