import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider, type RouteObject } from 'react-router-dom'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { queryClient } from '@/api/queryClient'
import type { Session } from '@/api/types'
import { RequirePermission } from '@/core/permissions/permissions'
import { RequireSalesLicense } from '@/core/license/license'
import { json, makeSession, problem } from '@/test/sessionFixture'
import { buildRoutes } from './routes'

type Handler = (init: RequestInit | undefined) => Response | Promise<Response>
let routes: Record<string, Handler>
const calls: { url: string; init?: RequestInit }[] = []

beforeAll(async () => {
  // jsdom has no matchMedia; the shell asks whether it is in drawer mode (narrow) or rail mode (wide).
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
  await loadNamespaces('common', 'account')
})

beforeEach(async () => {
  localStorage.clear()
  await setLanguage('en')
  queryClient.clear()
  calls.length = 0
  routes = {}
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      calls.push({ url, init })
      const handler = routes[url.replace(/\?.*$/, '')]
      if (!handler) return problem(404, 'not_found')
      return handler(init)
    }),
  )
})
afterEach(() => vi.unstubAllGlobals())

function signedIn(session: Session = makeSession()) {
  routes['/api/v1/session'] = () => json(session)
}

function mount(path: string, extra: RouteObject[] = []) {
  const router = createMemoryRouter(buildRoutes(extra), { initialEntries: [path] })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  )
  return router
}

describe('sign-in gate', () => {
  it('sends an anonymous visitor to the login page and remembers where they were going', async () => {
    routes['/api/v1/session'] = () => problem(401, 'unauthenticated')
    routes['/api/v1/auth/sign-in-options'] = () =>
      json({
        users: [{ username: 'sara', displayName: 'Sara Cashier', initials: 'SC' }],
        lastUsername: '',
        branding: makeSession().branding,
      })

    const router = mount('/orders?page=2')

    expect(await screen.findByText('Who is signing in?')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
    expect(router.state.location.search).toBe('?returnTo=%2Forders%3Fpage%3D2')
  })

  it('signs in with the 4-digit pad, then lands on the point of sale', async () => {
    routes['/api/v1/session'] = () => problem(401, 'unauthenticated')
    routes['/api/v1/auth/sign-in-options'] = () =>
      json({ users: [{ username: 'sara', displayName: 'Sara Cashier', initials: 'SC' }], lastUsername: 'sara', branding: makeSession().branding })
    routes['/api/v1/auth/login'] = () => json(makeSession())

    const router = mount('/login')
    await screen.findByText('Sara Cashier')
    for (const digit of ['1', '1', '1', '1']) fireEvent.click(screen.getByRole('button', { name: digit }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/pos'))
    const login = calls.find((c) => c.url === '/api/v1/auth/login')!
    expect(JSON.parse(String(login.init?.body))).toEqual({ username: 'sara', pin: '1111', rememberMe: false })
    expect(new Headers(login.init?.headers).get('X-NexPOS-Client')).toBe('web') // the CSRF header on every non-GET
  })

  it('shows the server message on a wrong PIN and stays on the login page', async () => {
    routes['/api/v1/session'] = () => problem(401, 'unauthenticated')
    routes['/api/v1/auth/sign-in-options'] = () =>
      json({ users: [{ username: 'sara', displayName: 'Sara Cashier', initials: 'SC' }], lastUsername: 'sara', branding: makeSession().branding })
    routes['/api/v1/auth/login'] = () => problem(401, 'invalid_credentials', 'Invalid username or PIN')

    const router = mount('/login')
    await screen.findByText('Sara Cashier')
    for (const digit of ['9', '9', '9', '9']) fireEvent.click(screen.getByRole('button', { name: digit }))

    expect(await screen.findByText('Invalid username or PIN')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/login')
  })
})

describe('shell', () => {
  it('draws the rail from the session: router links for migrated pages, plain anchors for the rest', async () => {
    signedIn()
    mount('/orders', [{ path: 'orders', element: <p>orders page</p> }])

    expect(await screen.findByText('orders page')).toBeInTheDocument()
    const rail = screen.getByRole('navigation', { name: 'Navigation' })
    expect(within(rail).getByRole('link', { name: /Orders/ })).toHaveAttribute('href', '/orders')
    expect(within(rail).getByRole('link', { name: /Point of Sale/ })).toHaveAttribute('href', '/en/POS')
    expect(within(rail).getByRole('link', { name: /Orders/ })).toHaveAttribute('aria-current', 'page')
  })

  it('shows the user, the drawer chip and the unread count', async () => {
    signedIn()
    mount('/orders', [{ path: 'orders', element: <p>orders page</p> }])

    await screen.findByText('orders page')
    expect(screen.getByText('Sara Cashier')).toBeInTheDocument()
    expect(screen.getByText('Cashier')).toBeInTheDocument()
    expect(screen.getByText(/Drawer open/)).toHaveTextContent('100.00')
    expect(screen.getByRole('link', { name: /Notifications \(3 unread\)/ })).toHaveAttribute('href', '/en/Notifications')
  })

  it('hides the drawer chip for people who cannot use a drawer', async () => {
    signedIn(makeSession({ drawer: undefined }))
    mount('/orders', [{ path: 'orders', element: <p>orders page</p> }])
    await screen.findByText('orders page')
    expect(screen.queryByText(/Drawer/)).not.toBeInTheDocument()
  })

  it('a rail entry with no React page yet gets the placeholder, and a registered module route wins over it', async () => {
    signedIn()
    mount('/pos')
    expect(await screen.findByText('This page has not moved to the new app yet. You can keep using the current page.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open the current page' })).toHaveAttribute('href', '/en/POS')
  })

  it('unknown addresses are a 404 inside the shell', async () => {
    signedIn()
    mount('/nope')
    expect(await screen.findByText('Page not found')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Navigation' })).toBeInTheDocument()
  })

  it('a bare route renders without the rail', async () => {
    signedIn()
    mount('/screen', [{ path: 'screen', handle: { layout: 'bare' }, element: <p>full screen</p> }])
    expect(await screen.findByText('full screen')).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Navigation' })).not.toBeInTheDocument()
  })

  it('a crash in one page leaves the rail usable', async () => {
    signedIn()
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    function Boom(): never {
      throw new Error('boom')
    }
    mount('/orders', [{ path: 'orders', element: <Boom /> }])
    expect(await screen.findByText('Something went wrong')).toBeInTheDocument()
    expect(screen.getByRole('navigation', { name: 'Navigation' })).toBeInTheDocument()
    consoleError.mockRestore()
  })

  it('opens the command palette with Ctrl+K and lists only the pages in the session', async () => {
    signedIn()
    mount('/orders', [{ path: 'orders', element: <p>orders page</p> }])
    await screen.findByText('orders page')

    fireEvent.keyDown(window, { key: 'k', ctrlKey: true })
    const dialog = await screen.findByRole('dialog', { name: 'Jump to…' })
    expect(within(dialog).getAllByRole('button').map((b) => b.textContent)).toEqual([
      'Point of SaleSales',
      'OrdersSales',
      'NotificationsInsights',
    ])
    const search = within(dialog).getByRole('textbox')
    fireEvent.change(search, { target: { value: 'ord' } })
    expect(within(dialog).getAllByRole('button')).toHaveLength(1)
    fireEvent.keyDown(search, { key: 'Enter' })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })
})

describe('licence', () => {
  it('shows the renewal banner while selling still works', async () => {
    signedIn(
      makeSession({
        license: { status: 'GracePeriod', allowsSales: true, showsWarning: true, enforcementDisabled: false, title: 'License renewal needed', message: 'Renew now.', severity: 'warning' },
      }),
    )
    mount('/orders', [{ path: 'orders', element: <p>orders page</p> }])
    expect(await screen.findByRole('alert')).toHaveTextContent('License renewal needed Renew now.')
  })

  it('sends sales routes to the licence-stopped page once selling is stopped, but leaves other pages open', async () => {
    signedIn(
      makeSession({
        license: { status: 'Expired', allowsSales: false, showsWarning: false, enforcementDisabled: false, title: 'License expired', message: 'Selling is stopped.', severity: 'danger' },
      }),
    )
    mount('/sell', [
      { element: <RequireSalesLicense />, children: [{ path: 'sell', element: <p>checkout</p> }] },
      { path: 'orders', element: <p>orders page</p> },
    ])
    expect(await screen.findByText('Selling is stopped')).toBeInTheDocument()
    expect(screen.queryByText('checkout')).not.toBeInTheDocument()
  })
})

describe('permissions', () => {
  it('bounces a guarded route to the forbidden page', async () => {
    signedIn() // holds only Orders.View
    mount('/users', [{ element: <RequirePermission permission="Users.View" />, children: [{ path: 'users', element: <p>users page</p> }] }])
    expect(await screen.findByText('Access Denied')).toBeInTheDocument()
    expect(screen.queryByText('users page')).not.toBeInTheDocument()
  })

  it('lets a holder through', async () => {
    signedIn(makeSession({ permissions: ['Users.View'] }))
    mount('/users', [{ element: <RequirePermission permission="Users.View" />, children: [{ path: 'users', element: <p>users page</p> }] }])
    expect(await screen.findByText('users page')).toBeInTheDocument()
  })
})

describe('language', () => {
  it('flips direction and re-fetches the session so the nav labels are re-localized', async () => {
    signedIn()
    mount('/orders', [{ path: 'orders', element: <p>orders page</p> }])
    await screen.findByText('orders page')
    const before = calls.filter((c) => c.url === '/api/v1/session').length

    fireEvent.click(screen.getByRole('button', { name: 'عربي' }))

    await waitFor(() => expect(document.documentElement.dir).toBe('rtl'))
    await waitFor(() => expect(calls.filter((c) => c.url === '/api/v1/session').length).toBeGreaterThan(before))
    const last = [...calls].reverse().find((c) => c.url === '/api/v1/session')!
    expect(new Headers(last.init?.headers).get('Accept-Language')).toBe('ar')
  })
})
