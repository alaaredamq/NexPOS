import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const category = (id: number, name: string, overrides = {}) => ({
  id,
  name,
  description: `${name} things`,
  isActive: true,
  productCount: 3,
  createdAt: '2026-01-05T10:00:00Z',
  ...overrides,
})

const list = (items: ReturnType<typeof category>[]) => ({ items, total: items.length, page: 1, pageSize: 25 })
const summary = { total: 2, active: 1, inactive: 1, products: 7 }

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'categories')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/categories'] = () => json(list([category(1, 'Drinks'), category(2, 'Snacks', { isActive: false, productCount: 4 })]))
  api.routes['GET /api/v1/categories/summary'] = () => json(summary)
  return mountModule(path, routes)
}

const ADMIN = [Permissions.Categories_View, Permissions.Categories_Create, Permissions.Categories_Edit, Permissions.Categories_Delete]

describe('categories list', () => {
  it('shows the rows, the status badges and the four tiles', async () => {
    open('/categories', ...ADMIN)

    expect(await screen.findByRole('link', { name: 'Drinks' })).toHaveAttribute('href', '/categories/1')
    expect(screen.getByRole('link', { name: 'Snacks' })).toBeInTheDocument()
    expect(screen.getByText('Inactive', { selector: 'span' })).toBeInTheDocument()
    expect(await screen.findByText('Total Categories')).toBeInTheDocument()
    expect(await screen.findByText('7')).toBeInTheDocument() // products across all categories
  })

  it('asks the API for the sort the user clicked, and keeps it in the URL', async () => {
    const router = open('/categories', ...ADMIN)
    await screen.findByRole('link', { name: 'Drinks' })

    fireEvent.click(screen.getByRole('button', { name: /Products/ }))

    await waitFor(() => expect(router.state.location.search).toBe('?sort=productCount&dir=asc'))
    await waitFor(() => expect(api.calls.some((c) => c.url.includes('sort=productCount') && c.url.includes('dir=asc'))).toBe(true))
  })

  it('a cashier who may only view sees no add button and no row actions', async () => {
    open('/categories', Permissions.Categories_View)

    await screen.findByRole('link', { name: 'Drinks' })

    expect(screen.queryByRole('button', { name: 'Add Category' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })

  it('without Categories.View the route bounces to the forbidden page', async () => {
    const router = open('/categories', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('create and edit', () => {
  it('refuses a one-letter name in the dialog without calling the API', async () => {
    open('/categories', ...ADMIN)
    await screen.findByRole('link', { name: 'Drinks' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Add Category' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add Category' })
    fireEvent.change(within(dialog).getByLabelText(/Category name/), { target: { value: 'x' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Enter a name of 2 to 100 characters.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('posts the trimmed name, sends an empty description as null, then refreshes the list', async () => {
    open('/categories', ...ADMIN)
    api.routes['POST /api/v1/categories'] = () => json(category(9, 'Bakery'), 201)
    await screen.findByRole('link', { name: 'Drinks' })
    const listCalls = () => api.calls.filter((c) => c.method === 'GET' && c.url.startsWith('/api/v1/categories?')).length
    const before = listCalls()

    fireEvent.click(screen.getAllByRole('button', { name: 'Add Category' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add Category' })
    fireEvent.change(within(dialog).getByLabelText(/Category name/), { target: { value: '  Bakery  ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Bakery', description: null, isActive: true }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await waitFor(() => expect(listCalls()).toBeGreaterThan(before))
    expect(await screen.findByText('Category created successfully!')).toBeInTheDocument()
  })

  it('puts a server validation error on the name field and keeps the dialog open', async () => {
    open('/categories', ...ADMIN)
    api.routes['PUT /api/v1/categories/1'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { name: ['Name already used'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await screen.findByRole('link', { name: 'Drinks' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Edit Category' })
    expect(within(dialog).getByLabelText(/Category name/)).toHaveValue('Drinks')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Name already used')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Edit Category' })).toBeInTheDocument()
  })
})

describe('delete', () => {
  it('confirms, deletes, and says so', async () => {
    open('/categories', ...ADMIN)
    api.routes['DELETE /api/v1/categories/1'] = () => new Response(null, { status: 204 })
    await screen.findByRole('link', { name: 'Drinks' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Confirm Delete' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'DELETE' && c.url === '/api/v1/categories/1')).toBe(true))
    expect(await screen.findByText('Category deleted successfully!')).toBeInTheDocument()
  })

  it('explains in the dialog why a category that products still use cannot be deleted', async () => {
    open('/categories', ...ADMIN)
    api.routes['DELETE /api/v1/categories/1'] = () => problem(409, 'category_in_use', 'server words')
    await screen.findByRole('link', { name: 'Drinks' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Confirm Delete' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await within(dialog).findByText(/still used by products/)).toBeInTheDocument()
    expect(screen.queryByText('Category deleted successfully!')).not.toBeInTheDocument()
  })
})

describe('details', () => {
  const details = {
    category: category(1, 'Drinks'),
    activeProducts: 2,
    totalStock: 15,
    totalValue: 42.5,
    currencyNotation: '$',
    products: [
      { id: 10, name: 'Cola', barcode: '123', sellingPrice: 1.5, currencyNotation: '$', stockQuantity: 2, minimumStock: 5, isActive: true },
      { id: 11, name: 'Water', barcode: '456', sellingPrice: 0.5, currencyNotation: '$', stockQuantity: 50, minimumStock: 5, isActive: true },
    ],
  }

  it('shows the products and the stock figures', async () => {
    open('/categories/1', ...ADMIN)
    api.routes['GET /api/v1/categories/1'] = () => json(details)

    expect(await screen.findByRole('heading', { name: 'Drinks' })).toBeInTheDocument()
    expect(await screen.findByText('Cola')).toBeInTheDocument()
    expect(screen.getByText(/42.50/)).toBeInTheDocument()
    expect(screen.getByText('Total Stock')).toBeInTheDocument()
  })

  it('shows the not-found page for a category that does not exist', async () => {
    open('/categories/99', ...ADMIN)
    api.routes['GET /api/v1/categories/99'] = () => problem(404, 'not_found')

    expect(await screen.findByText(/not found|doesn't exist|no longer/i)).toBeInTheDocument()
  })
})
