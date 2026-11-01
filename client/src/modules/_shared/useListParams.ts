// Shared by the inventory and suppliers modules (C3). Categories keeps its own copy; promotion to ui/ happens in Cutover (Z).
import { useCallback, useMemo } from 'react'
import { useSearchParams } from 'react-router-dom'
import type { SortState } from '@/ui/DataTable'

const DEFAULT_PAGE_SIZE = 25
export const PAGE_SIZES = [10, 25, 50, 100]

export interface ListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
}

/**
 * The list state of a page lives in the URL (`?page=2&sort=name&dir=desc&q=milk`): a refresh, the back button and a
 * copied link all land on the same rows. Defaults are left out of the URL to keep it short.
 */
export function useListParams(defaultSort: SortState) {
  const [search, setSearch] = useSearchParams()

  const params = useMemo<ListParams>(() => {
    const page = Number(search.get('page'))
    const pageSize = Number(search.get('pageSize'))
    const dir = search.get('dir')
    return {
      page: Number.isInteger(page) && page >= 1 ? page : 1,
      pageSize: PAGE_SIZES.includes(pageSize) ? pageSize : DEFAULT_PAGE_SIZE,
      sort: search.get('sort') ?? defaultSort.key,
      dir: dir === 'desc' || dir === 'asc' ? dir : defaultSort.dir,
      q: search.get('q') ?? '',
    }
  }, [search, defaultSort.key, defaultSort.dir])

  /** Changing the filter, sort or page size goes back to page 1 unless the patch says which page. */
  const update = useCallback(
    (patch: Partial<ListParams>) => {
      const merged = { ...params, page: 1, ...patch }
      const next = new URLSearchParams()
      if (merged.page > 1) next.set('page', String(merged.page))
      if (merged.pageSize !== DEFAULT_PAGE_SIZE) next.set('pageSize', String(merged.pageSize))
      if (merged.sort !== defaultSort.key || merged.dir !== defaultSort.dir) {
        next.set('sort', merged.sort)
        next.set('dir', merged.dir)
      }
      if (merged.q) next.set('q', merged.q)
      setSearch(next, { replace: true })
    },
    [params, setSearch, defaultSort.key, defaultSort.dir],
  )

  const sort: SortState = { key: params.sort, dir: params.dir }
  return { params, sort, update }
}
