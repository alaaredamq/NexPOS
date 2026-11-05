import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router-dom'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { queryClient } from '@/api/queryClient'
import { buildRoutes } from '@/app/routes'
import { UiHost } from '@/ui/UiHost'
import { json, makeSession, problem } from '@/test/sessionFixture'
import routes from './routes'

type Handler = (init: RequestInit | undefined, url: string) => Response | Promise<Response>
let handlers: Record<string, Handler>
const calls: string[] = []

const entry = (id: number, over: Record<string, unknown> = {}) => ({ id, createdAt: '2026-11-05T10:00:00Z', fields: [], fromImport: false, userName: 'layla', ...over })

const history = (items: unknown[], total = items.length) => ({
  productId: 7,
  productName: 'Cola 330ml',
  unit: 'piece',
  currencyNotation: '$',
  currentStock: 12,
  totalSold: 2,
  totalReturned: 0,
  totalAdded: 5,
  items: { items, total, page: 1, pageSize: 25 },
})

const rows = [
  entry(4, { type: 'stockRemoved', quantityChange: -1, stockBefore: 13, stockAfter: 12, reason: 'Damaged' }),
  entry(3, { type: 'sold', quantityChange: -2, stockBefore: 15, stockAfter: 13, orderId: 42, orderNumber: 'ORD-000042', userName: 'sara' }),
  entry(2, { type: 'stockAdded', quantityChange: 5, stockBefore: 10, stockAfter: 15, reason: 'Delivery from Acme' }),
  entry(1, { type: 'created', stockAfter: 10 }),
]

beforeAll(async () => {
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
  await loadNamespaces('common', 'products')
})

beforeEach(async () => {
  localStorage.clear()
  await setLanguage('en')
  queryClient.clear()
  calls.length = 0
  handlers = { '/api/v1/products/7/history': () => json(history(rows)) }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      calls.push(url)
      const handler = handlers[url.replace(/\?.*$/, '')]
      return handler ? handler(init, url) : problem(404, 'not_found')
    }),
  )
})
afterEach(() => vi.unstubAllGlobals())

function mount(path: string, permissions: string[]) {
  handlers['/api/v1/session'] = () => json(makeSession({ permissions }))
  const router = createMemoryRouter(buildRoutes(routes), { initialEntries: [path] })
  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <UiHost />
    </QueryClientProvider>,
  )
  return router
}

describe('product history page', () => {
  it('lists what was done, newest first, in plain sentences with the stock before and after', async () => {
    mount('/products/7/history', ['Products.View', 'Orders.View'])

    expect(await screen.findByRole('heading', { name: 'History: Cola 330ml' })).toBeInTheDocument()
    const list = screen.getByRole('list')
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(4)
    expect(items[0]).toHaveTextContent('1 units removed from stock')
    expect(items[0]).toHaveTextContent('Stock: 13 → 12')
    expect(items[0]).toHaveTextContent('Reason: Damaged')
    expect(items[1]).toHaveTextContent('2 units sold in sale ORD-000042')
    expect(items[1]).toHaveTextContent('By sara')
    expect(items[2]).toHaveTextContent('5 units added to stock')
    expect(items[2]).toHaveTextContent('Reason: Delivery from Acme')
    expect(items[3]).toHaveTextContent('Product added with 10 units in stock')
  })

  it('shows the lifetime totals above the list', async () => {
    mount('/products/7/history', ['Products.View'])

    await screen.findByText('Sold in total')
    expect(screen.getByText('In stock now').nextElementSibling).toHaveTextContent('12 units')
    expect(screen.getByText('Sold in total').nextElementSibling).toHaveTextContent('2 units')
    expect(screen.getByText('Added in total').nextElementSibling).toHaveTextContent('5 units')
  })

  it('links a sale to its order only for someone who may open orders', async () => {
    mount('/products/7/history', ['Products.View'])
    await screen.findByText('2 units sold in sale ORD-000042')

    expect(screen.queryByRole('link', { name: 'ORD-000042' })).not.toBeInTheDocument()
  })

  it('asks the API for the chosen kind, keeps it in the URL and starts again at page 1', async () => {
    const router = mount('/products/7/history?page=2', ['Products.View', 'Orders.View'])
    await screen.findByText('Sold in total')

    fireEvent.click(screen.getByRole('button', { name: 'Sales' }))

    await waitFor(() => expect(router.state.location.search).toBe('?kind=sales'))
    await waitFor(() => expect(calls.some((url) => url.includes('/history?') && url.includes('kind=sales') && url.includes('page=1'))).toBe(true))
  })

  it('explains an empty history', async () => {
    handlers['/api/v1/products/7/history'] = () => json(history([]))
    mount('/products/7/history', ['Products.View'])

    expect(await screen.findByText('Nothing recorded for this product yet')).toBeInTheDocument()
    expect(screen.getByText(/History starts from the day this feature was added/)).toBeInTheDocument()
  })

  it('shows "not found" for an unknown product', async () => {
    handlers['/api/v1/products/99/history'] = () => problem(404, 'not_found')
    mount('/products/99/history', ['Products.View'])

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('sends a user without Products.View to the 403 page', async () => {
    const router = mount('/products/7/history', ['Orders.View'])

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})
