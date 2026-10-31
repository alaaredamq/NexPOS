import { useCallback } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { queryKeys } from '@/api/queryClient'
import type { Session } from '@/api/types'
import { sessionApi } from './sessionApi'

/** Drawer chip, unread badge, licence state and role edits all ride on this refresh (0A: permissions are re-read each request). */
export const SESSION_REFRESH_MS = 60_000

const sessionQuery = {
  queryKey: queryKeys.session,
  queryFn: ({ signal }: { signal: AbortSignal }) => sessionApi.get(signal),
}

/** Owns the fetching; mounted once, by `RequireAuth`. */
export function useSessionQuery() {
  return useQuery({
    ...sessionQuery,
    refetchInterval: SESSION_REFRESH_MS,
    refetchOnWindowFocus: true,
    staleTime: 10_000,
  })
}

/**
 * The signed-in session. Only valid under `RequireAuth`, which does not render its children until the
 * bootstrap has succeeded - so modules can read `useSession().permissions` without null checks.
 * (A disabled observer: it subscribes to the cache so the 60 s refresh re-renders, but never fetches itself.)
 */
export function useSession(): Session {
  const { data } = useQuery({ ...sessionQuery, enabled: false })
  if (!data) throw new Error('useSession() was used outside an authenticated route.')
  return data
}

/** Re-fetch now (after a language switch the nav labels need re-localizing; after a drawer change the chip is stale). */
export function useRefreshSession(): () => Promise<void> {
  const queryClient = useQueryClient()
  return useCallback(() => queryClient.invalidateQueries({ queryKey: queryKeys.session }), [queryClient])
}
