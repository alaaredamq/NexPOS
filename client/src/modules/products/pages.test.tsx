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
const calls: { url: string; init?: RequestInit }[] = []

const admin = ['Products.View', 'Products.Create', 'Products.Edit', 'Products.Delete']

const item = (id: number, over: Record<string, unknown> = {}) => ({
  id,
  name: `Product ${id}`,
  barcode: `B${id}`,
  categoryId: 1,
  categoryName: 'Drinks',
  supplierId: 1,
  supplierName: 'No Supplier',
  currencyId: 1,
  currencyNotation: '$',
  purchasePrice: 2,
  sellingPrice: 3.5,
  stockQuantity: 10,
  unit: 'piece',
  isActive: true,
  isPack: false,
  stockLevel: 'ok',
  ...over,
})

const lookups = {
  categories: [{ id: 1, name: 'Drinks' }],
  suppliers: [{ id: 1, name: 'No Supplier', discountRate: 0 }],
  currencies: [{ id: 1, code: 'USD', notation: '$', isDefault: true }],
  defaultSupplierId: 1,
  defaultCurrencyId: 1,
}

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
  handlers = {
    '/api/v1/products/summary': () => json({ total: 2, active: 2, lowStock: 1, outOfStock: 0, lowStockThreshold: 5 }),
    '/api/v1/products/lookups': () => json(lookups),
    '/api/v1/products': () =>
      json({ items: [item(1), item(2, { stockQuantity: 3, stockLevel: 'low' })], total: 2, page: 1, pageSize: 25 }),
  }
  vi.stubGlobal(
    'fetch',
    vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      calls.push({ url, init })
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

describe('products list', () => {
  it('shows the rows, the tiles and the actions the role may use', async () => {
    mount('/products', admin)

    expect(await screen.findByText('Product 1')).toBeInTheDocument()
    expect(screen.getByText('Product 2')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add Product' })).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Edit Product' })).toHaveLength(2)
    expect(screen.getAllByRole('button', { name: 'Delete Product' })).toHaveLength(2)
    expect(screen.getByText('5 units or fewer')).toBeInTheDocument() // the threshold comes from the API
  })

  it('hides add / edit / delete / import from a viewer but still lets them print labels', async () => {
    mount('/products', ['Products.View'])

    await screen.findByText('Product 1')
    expect(screen.queryByRole('button', { name: 'Add Product' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit Product' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete Product' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Import' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Print label' })).toHaveLength(2)
  })

  it('sends a user without Products.View to the 403 page', async () => {
    const router = mount('/products', ['Orders.View'])

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('puts filters in the URL and asks the API for them, starting again at page 1', async () => {
    const router = mount('/products?page=2', admin)
    await screen.findByText('Product 1')

    fireEvent.click(screen.getByRole('button', { name: 'Low Stock' }))

    await waitFor(() => expect(router.state.location.search).toBe('?stock=low'))
    await waitFor(() => expect(calls.some((c) => c.url.startsWith('/api/v1/products?') && c.url.includes('stock=low') && c.url.includes('page=1'))).toBe(true))
  })

  it('deletes after a confirmation and refreshes', async () => {
    handlers['/api/v1/products/1'] = (init) => (init?.method === 'DELETE' ? new Response(null, { status: 204 }) : problem(405, 'method_not_allowed'))
    mount('/products', admin)
    await screen.findByText('Product 1')

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete Product' })[0]!)
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(calls.some((c) => c.init?.method === 'DELETE' && c.url === '/api/v1/products/1')).toBe(true))
    const del = calls.find((c) => c.init?.method === 'DELETE')!
    const headers = (del.init ?? {}).headers as Record<string, string>
    expect(headers['X-NexPOS-Client']).toBe('web')
  })
})

describe('product form', () => {
  it('does not call the API for an incomplete form and shows each missing field', async () => {
    mount('/products/new', admin)
    await screen.findByText('Product Information')

    fireEvent.click(screen.getByRole('button', { name: 'Create Product' }))

    expect(await screen.findByText('Barcode is required')).toBeInTheDocument()
    expect(screen.getByText('Category is required')).toBeInTheDocument()
    expect(screen.getByText('Purchase price must be greater than 0')).toBeInTheDocument()
    expect(calls.some((c) => c.init?.method === 'POST')).toBe(false)
  })

  it("puts the server's barcode error on the barcode field", async () => {
    handlers['/api/v1/products'] = (init) =>
      init?.method === 'POST'
        ? new Response(
            JSON.stringify({ status: 400, code: 'validation_failed', title: 'Please correct the highlighted fields.', traceId: 't', errors: { barcode: ['A product with this barcode already exists.'] } }),
            { status: 400, headers: { 'Content-Type': 'application/problem+json' } },
          )
        : json({ items: [], total: 0, page: 1, pageSize: 25 })
    mount('/products/new', admin)
    await screen.findByText('Product Information')

    fireEvent.change(screen.getByLabelText(/Product Name/), { target: { value: 'Water' } })
    fireEvent.change(screen.getByLabelText(/^Barcode/), { target: { value: '111' } })
    fireEvent.change(screen.getByLabelText(/Purchase Price/), { target: { value: '1' } })
    fireEvent.change(screen.getByLabelText(/Selling Price/), { target: { value: '2' } })
    fireEvent.focus(screen.getByRole('combobox', { name: /^Category/ }))
    fireEvent.click(screen.getByRole('combobox', { name: /^Category/ }))
    fireEvent.click(await screen.findByRole('option', { name: 'Drinks' }))
    fireEvent.click(screen.getByRole('button', { name: 'Create Product' }))

    expect(await screen.findByText('A product with this barcode already exists.')).toBeInTheDocument()
    const post = calls.find((c) => c.init?.method === 'POST')!
    expect(JSON.parse(String(post.init?.body))).toMatchObject({ name: 'Water', barcode: '111', purchasePrice: 1, sellingPrice: 2, categoryId: 1, supplierId: 1, currencyId: 1 })
  })

  it('shows "not found" for an unknown product', async () => {
    handlers['/api/v1/products/99'] = () => problem(404, 'not_found', 'Product not found.')
    mount('/products/99/edit', admin)

    expect(await screen.findByText('Product not found.')).toBeInTheDocument()
  })

  it('guards the create and edit routes by permission', async () => {
    const router = mount('/products/new', ['Products.View'])

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})
