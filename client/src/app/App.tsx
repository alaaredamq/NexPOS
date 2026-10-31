import { Suspense, useEffect, useState } from 'react'
import { createBrowserRouter, RouterProvider, type RouteObject } from 'react-router-dom'
import { QueryClientProvider } from '@tanstack/react-query'
import { onApiError } from '@/api/http'
import { queryClient, queryKeys } from '@/api/queryClient'
import { Loader } from '@/ui/Loader'
import { UiHost } from '@/ui/UiHost'
import { buildRoutes } from './routes'

/**
 * Codes after which the session payload is stale: a lost cookie (the boot probe then answers 401 and `RequireAuth` redirects),
 * a stopped licence (the banner and the sales guard read it) and a permission that changed under a long-lived page.
 */
const SESSION_STALE_CODES = new Set(['unauthenticated', 'license_sales_stopped', 'forbidden'])

export function createAppRouter(routes: RouteObject[] = buildRoutes()) {
  return createBrowserRouter(routes, { basename: '/app' })
}

export function App({ router: provided }: { router?: ReturnType<typeof createAppRouter> } = {}) {
  const [router] = useState(() => provided ?? createAppRouter())

  useEffect(
    () =>
      onApiError((error) => {
        if (SESSION_STALE_CODES.has(error.code)) void queryClient.invalidateQueries({ queryKey: queryKeys.session })
      }),
    [],
  )

  return (
    <QueryClientProvider client={queryClient}>
      <Suspense fallback={<Loader fullSection size="lg" />}>
        <RouterProvider router={router} />
      </Suspense>
      <UiHost />
    </QueryClientProvider>
  )
}
