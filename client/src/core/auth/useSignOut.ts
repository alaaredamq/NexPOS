import { useCallback } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { queryKeys } from '@/api/queryClient'
import { loginUrl } from './returnTo'
import { sessionApi } from './sessionApi'

/**
 * Ends the session on the server (cookie cleared, audit row written), drops every cached query so the next user
 * cannot see the last one's data, and shows the login page - remembering the current route unless `forget` is set.
 */
export function useSignOut() {
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()

  return useCallback(
    async (options: { reason?: 'idle'; forget?: boolean } = {}) => {
      const here = `${location.pathname}${location.search}`
      try {
        await sessionApi.logout()
      } catch {
        // Server unreachable: still leave the screen. The cookie dies on its own (idle: 8 h); nothing else we can do.
      }
      await navigate(loginUrl(options.forget ? undefined : here, options.reason), { replace: true })
      queryClient.removeQueries({ queryKey: queryKeys.session })
      queryClient.clear()
    },
    [location.pathname, location.search, navigate, queryClient],
  )
}
