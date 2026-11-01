import { render } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router-dom'
import { vi } from 'vitest'
import { buildRoutes } from '@/app/routes'
import { queryClient } from '@/api/queryClient'
import type { Session } from '@/api/types'
import { UiHost } from '@/ui/UiHost'
import { json, makeSession, problem } from '@/test/sessionFixture'

export type Handler = (init: RequestInit | undefined, url: string) => Response | Promise<Response>

export interface FakeApi {
  /** `routes['GET /api/v1/categories'] = () => json(...)`; the key is `METHOD path` without the query string. */
  routes: Record<string, Handler>
  calls: { method: string; url: string; body?: unknown }[]
}

/** Stubs `fetch` with a tiny router. Unknown routes answer 404 `not_found`. Call `vi.unstubAllGlobals()` after the test. */
export function fakeApi(session: Session = makeSession()): FakeApi {
  const api: FakeApi = { routes: { 'GET /api/v1/session': () => json(session) }, calls: [] }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      const method = init?.method ?? 'GET'
      api.calls.push({ method, url, body: typeof init?.body === 'string' ? JSON.parse(init.body) : undefined })
      const handler = api.routes[`${method} ${url.replace(/\?.*$/, '')}`]
      return handler ? handler(init, url) : problem(404, 'not_found')
    }),
  )
  return api
}

/** A session holding exactly these permissions. */
export function sessionWith(...permissions: string[]): Session {
  return makeSession({ permissions })
}

/** Renders the real app routes (sign-in gate, shell, guards) with the module's own routes at `path`. */
export function mountModule(path: string, moduleRoutes: RouteObject[]) {
  queryClient.clear()
  const router = createMemoryRouter(buildRoutes(moduleRoutes), { initialEntries: [path] })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <UiHost />
    </QueryClientProvider>,
  )
  return router
}

export function stubMatchMedia() {
  window.matchMedia = ((query: string) => ({
    matches: false,
    media: query,
    addEventListener: () => {},
    removeEventListener: () => {},
    addListener: () => {},
    removeListener: () => {},
    onchange: null,
    dispatchEvent: () => false,
  })) as unknown as typeof window.matchMedia
}

export { json, problem }
