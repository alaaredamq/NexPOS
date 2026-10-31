import { QueryClient } from '@tanstack/react-query'
import { isApiError } from './http'

/** Query keys the shell owns. Modules define their own next to their hooks. */
export const queryKeys = {
  session: ['session'] as const,
  signInOptions: ['sign-in-options'] as const,
}

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: true,
      // A 4xx will not get better by asking again (401 sends the user to the login page; 403/404/409 are answers).
      retry: (failureCount, error) => !(isApiError(error) && error.status >= 400 && error.status < 500) && failureCount < 2,
    },
    mutations: { retry: false },
  },
})
