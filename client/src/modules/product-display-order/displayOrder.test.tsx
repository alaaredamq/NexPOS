import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const item = (id: number, name: string, order: number, overrides = {}) => ({
  id,
  productId: id + 100,
  order,
  isActive: true,
  name,
  description: null,
  image: null,
  categoryName: 'Drinks',
  price: 2,
  currencyNotation: '$',
  stockQuantity: 20,
  isLowStock: false,
  ...overrides,
})

const board = (items: ReturnType<typeof item>[]) => ({
  items,
  total: items.length,
  active: items.length,
  inactive: 0,
  lowStock: items.filter((i) => i.isLowStock).length,
})

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'productDisplayOrder')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/product-display-order'] = () =>
    json(board([item(1, 'Cola', 1), item(2, 'Water', 2), item(3, 'Juice', 3, { isLowStock: true, stockQuantity: 1 })]))
  return mountModule('/product-display-order', routes)
}

const names = () => screen.getAllByRole('row').slice(1).map((row) => row.querySelector('strong')?.textContent)

describe('display order page', () => {
  it('lists the products in order and starts with nothing to save', async () => {
    open(Permissions.MainScreenProducts_Manage)

    await screen.findByText('Cola')
    expect(names()).toEqual(['Cola', 'Water', 'Juice'])
    expect(screen.getByRole('button', { name: 'Save Order' })).toBeDisabled()
    expect(screen.getByText('Low Stock')).toBeInTheDocument()
  })

  it('without MainScreenProducts.Manage the route bounces to the forbidden page', async () => {
    const router = open(Permissions.Categories_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('moving a row enables Save, and Save sends the whole order', async () => {
    open(Permissions.MainScreenProducts_Manage)
    api.routes['PUT /api/v1/product-display-order/order'] = () => new Response(null, { status: 204 })
    await screen.findByText('Cola')

    fireEvent.click(screen.getAllByRole('button', { name: 'Move down' })[0]!)

    expect(names()).toEqual(['Water', 'Cola', 'Juice'])
    const save = screen.getByRole('button', { name: 'Save Order' })
    expect(save).toBeEnabled()
    fireEvent.click(save)

    await waitFor(() => expect(api.calls.find((c) => c.method === 'PUT')?.body).toEqual({ ids: [2, 1, 3] }))
    expect(await screen.findByText('Display order updated successfully')).toBeInTheDocument()
  })

  it('the first row cannot move up and the last cannot move down', async () => {
    open(Permissions.MainScreenProducts_Manage)
    await screen.findByText('Cola')

    expect(screen.getAllByRole('button', { name: 'Move up' })[0]).toBeDisabled()
    expect(screen.getAllByRole('button', { name: 'Move down' })[2]).toBeDisabled()
  })

  it('discard puts the saved order back', async () => {
    open(Permissions.MainScreenProducts_Manage)
    await screen.findByText('Cola')

    fireEvent.click(screen.getAllByRole('button', { name: 'Move down' })[0]!)
    fireEvent.click(screen.getByRole('button', { name: 'Discard changes' }))

    expect(names()).toEqual(['Cola', 'Water', 'Juice'])
    expect(screen.getByRole('button', { name: 'Save Order' })).toBeDisabled()
  })

  it('keeps the dialog-free error: a failed save shows the server message and keeps the arrangement', async () => {
    open(Permissions.MainScreenProducts_Manage)
    api.routes['PUT /api/v1/product-display-order/order'] = () => problem(500, 'internal_error', 'Could not save')
    await screen.findByText('Cola')

    fireEvent.click(screen.getAllByRole('button', { name: 'Move down' })[0]!)
    fireEvent.click(screen.getByRole('button', { name: 'Save Order' }))

    expect(await screen.findByText('Could not save')).toBeInTheDocument()
    expect(names()).toEqual(['Water', 'Cola', 'Juice'])
  })

  it('removing a product asks first, then deletes the row', async () => {
    open(Permissions.MainScreenProducts_Manage)
    api.routes['DELETE /api/v1/product-display-order/items/2'] = () => new Response(null, { status: 204 })
    await screen.findByText('Cola')

    fireEvent.click(screen.getAllByRole('button', { name: 'Remove' })[1]!)
    const dialog = await screen.findByRole('dialog', { name: 'Remove product?' })
    expect(within(dialog).getByText('Water')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Remove' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'DELETE' && c.url.endsWith('/items/2'))).toBe(true))
    expect(await screen.findByText('Product removed from display order')).toBeInTheDocument()
  })

  it('shows an empty state with an add button when nothing is on the main screen', async () => {
    open(Permissions.MainScreenProducts_Manage)
    api.routes['GET /api/v1/product-display-order'] = () => json(board([]))

    expect(await screen.findByText('No products on the main screen')).toBeInTheDocument()
  })
})

describe('add product dialog', () => {
  const available = { items: [{ id: 50, name: 'Chips', categoryName: 'Snacks', price: 1.25, currencyNotation: '$' }], hasMore: true }

  it('previews a few products, hints that search finds more, and adds the chosen one', async () => {
    open(Permissions.MainScreenProducts_Manage)
    api.routes['GET /api/v1/product-display-order/available-products'] = () => json(available)
    api.routes['POST /api/v1/product-display-order/items'] = () => json(item(9, 'Chips', 4), 201)
    await screen.findByText('Cola')

    fireEvent.click(screen.getAllByRole('button', { name: 'Add Product' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add Product to Display Order' })
    expect(await within(dialog).findByText('Chips')).toBeInTheDocument()
    expect(within(dialog).getByText('Showing the first products only. Search to find more.')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ productId: 50 }))
    expect(await screen.findByText('Product added to display order')).toBeInTheDocument()
  })

  it('quick add validates the price, then posts a generated-barcode product', async () => {
    open(Permissions.MainScreenProducts_Manage)
    api.routes['GET /api/v1/product-display-order/available-products'] = () => json({ items: [], hasMore: false })
    api.routes['GET /api/v1/product-display-order/quick-add-options'] = () =>
      json({ currencies: [{ id: 1, code: 'USD', notation: '$' }], defaultCurrencyId: 1 })
    api.routes['POST /api/v1/product-display-order/quick-add'] = () => json({ productId: 77, displayOrderId: 8, barcode: '2261101123456' }, 201)
    await screen.findByText('Cola')

    fireEvent.click(screen.getAllByRole('button', { name: 'Add Product' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add Product to Display Order' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Quick Add Product' }))
    fireEvent.change(await within(dialog).findByLabelText(/Product Name/), { target: { value: ' Gum ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create & Add Product' }))

    expect(await within(dialog).findByText('Price must be greater than 0')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)

    fireEvent.change(within(dialog).getByLabelText(/Price/), { target: { value: '0.5' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create & Add Product' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toMatchObject({ name: 'Gum', generateBarcode: true, barcode: null, price: 0.5, currencyId: 1 }))
    expect(await screen.findByText(/2261101123456/)).toBeInTheDocument()
  })
})
