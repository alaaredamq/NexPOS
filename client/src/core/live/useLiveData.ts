import { useEffect, useRef } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import type { Query } from '@tanstack/react-query'
import { http } from '@/api/http'

/** How often an open screen asks whether another PC changed something. A few bytes on the shop network; paused while the window is hidden. */
export const LIVE_POLL_MS = 3_000

/**
 * The screens that show numbers another PC can change (stock after a sale, a customer's balance, the drawer, today's totals). Everything
 * else (session, licence, notifications) has its own refresh, and edit forms are left alone so a reload can never overwrite what someone is typing.
 */
const LIVE_ROOTS = new Set(['pos', 'products', 'inventory', 'orders', 'customers', 'suppliers', 'purchases', 'cash-drawer', 'dashboard', 'receipts'])

export function isLiveQuery(query: Query): boolean {
  const [root, kind] = query.queryKey
  if (typeof root !== 'string' || !LIVE_ROOTS.has(root)) return false
  return !(root === 'products' && kind === 'detail') // the product edit form
}

interface LiveVersion {
  version: string
}

/**
 * Several PCs, one store database: when a till sells, the others must show the new quantities without anyone pressing refresh. The server moves
 * a version number whenever a write succeeds; this polls it and, when it changes, refetches the live screens that are open (and marks the closed
 * ones stale). Our own writes move it too, which costs one extra refetch; that is the price of never missing another PC's change.
 * Mounted once, behind the sign-in.
 */
export function useLiveData(enabled = true): void {
  const queryClient = useQueryClient()
  const seen = useRef<string | null>(null)

  const { data } = useQuery({
    queryKey: ['live', 'version'],
    enabled,
    queryFn: ({ signal }) => http.get<LiveVersion>('/live/version', { signal, quiet: true }),
    refetchInterval: LIVE_POLL_MS,
    refetchOnWindowFocus: true,
    staleTime: 0,
    gcTime: 0,
    retry: false,
  })

  const version = data?.version
  useEffect(() => {
    if (version === undefined) return
    const before = seen.current
    seen.current = version
    if (before !== null && before !== version) void queryClient.invalidateQueries({ predicate: isLiveQuery })
  }, [version, queryClient])
}
