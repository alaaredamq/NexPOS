import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { makeSession } from '@/test/sessionFixture'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { purchaseTotals } from './api'
import routes from './routes'

const listItem = (id: number, overrides = {}) => ({
  id,
  number: `PUR-00000${id}`,
  purchaseDate: '2026-11-03T10:00:00Z',
  supplierId: 1,
  supplierName: 'Acme',
  supplierReference: 'INV-77',
  currencyNotation: '$',
  itemCount: 2,
  total: 100,
  amountPaid: 40,
  amountDue: 60,
  status: 'received',
  ...overrides,
})

const detail = (overrides = {}) => ({
  id: 3,
  number: 'PUR-000003',
  purchaseDate: '2026-11-03T10:00:00Z',
  supplierId: 1,
  supplierName: 'Acme',
  supplierPhone: '70123456',
  supplierEmail: null,
  currencyId: 1,
  currencyNotation: '$',
  supplierReference: 'INV-77',
  notes: 'Second delivery',
  subtotal: 100,
  discountPercent: 0,
  discountAmount: 0,
  total: 100,
  amountPaid: 40,
  amountDue: 60,
  status: 'received',
  createdAt: '2026-11-03T10:00:00Z',
  createdBy: 'Sara Admin',
  cancelledAt: null,
  cancelledBy: null,
  cancelReason: null,
  items: [{ id: 1, productId: 9, productName: 'Olive oil 1L', barcode: '123', unit: 'piece', quantity: 10, unitCost: 10, lineTotal: 100, updatedCostPrice: true }],
  ...overrides,
})

const supplier = {
  id: 1,
  name: 'Acme',
  email: null,
  phone: '70123456',
  address: null,
  city: null,
  country: null,
  postalCode: null,
  discountRate: 10,
  isActive: true,
  currencyId: 1,
  currencyName: 'US Dollar',
  currencyNotation: '$',
  balance: 25,
  productCount: 3,
}

const product = {
  id: 9,
  name: 'Olive oil 1L',
  barcode: '123',
  categoryId: 1,
  categoryName: 'Pantry',
  supplierId: 1,
  supplierName: 'Acme',
  currencyId: 1,
  currencyNotation: '$',
  purchasePrice: 8,
  sellingPrice: 12,
  stockQuantity: 4,
  unit: 'piece',
  isActive: true,
  isPack: false,
  stockLevel: 'low',
}

const page = <T,>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 25 })
const summary = { count: 2, cancelledCount: 1, totals: [{ currencyId: 1, currencyNotation: '$', total: 200, paid: 80, due: 120 }] }

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'purchases', 'print')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

const VIEW = [Permissions.Purchases_View]
const ALL = [Permissions.Purchases_View, Permissions.Purchases_Create, Permissions.Purchases_Cancel, Permissions.Suppliers_View]

function open(path: string, ...permissions: string[]) {
  return openAs(path, sessionWith(...permissions))
}

function openAs(path: string, session: ReturnType<typeof sessionWith>) {
  api = fakeApi(session)
  api.routes['GET /api/v1/purchases'] = () => json(page([listItem(3), listItem(2, { status: 'cancelled', amountDue: 0, amountPaid: 100 })]))
  api.routes['GET /api/v1/purchases/summary'] = () => json(summary)
  api.routes['GET /api/v1/purchases/3'] = () => json(detail())
  api.routes['GET /api/v1/suppliers/1'] = () => json(supplier)
  api.routes['GET /api/v1/suppliers'] = () => json(page([supplier]))
  api.routes['GET /api/v1/products'] = () => json(page([product]))
  return mountModule(path, routes)
}

describe('purchaseTotals', () => {
  it('rounds each line, then takes the discount off the subtotal', () => {
    expect(purchaseTotals([{ quantity: 3, unitCost: 3.333 }, { quantity: 1, unitCost: 0.5 }], 10)).toEqual({ subtotal: 10.5, discount: 1.05, total: 9.45 })
  })

  it('is zero for an empty purchase', () => {
    expect(purchaseTotals([], 25)).toEqual({ subtotal: 0, discount: 0, total: 0 })
  })
})

describe('purchases list', () => {
  it('shows each purchase with what is left to pay, and the tiles per currency', async () => {
    open('/purchases', ...ALL)

    expect(await screen.findByRole('link', { name: 'PUR-000003' })).toHaveAttribute('href', '/purchases/3')
    const table = screen.getByRole('table')
    expect(within(table).getByText('Cancelled')).toBeInTheDocument()
    expect(within(table).getByText(/60\.00/)).toHaveAttribute('data-tone', 'owe')
    expect(await screen.findByText(/120\.00/)).toBeInTheDocument()
  })

  it('a viewer cannot start a purchase', async () => {
    open('/purchases', ...VIEW)
    await screen.findByRole('link', { name: 'PUR-000003' })

    expect(screen.queryByRole('button', { name: 'New purchase' })).not.toBeInTheDocument()
  })

  it('the new-purchase page is closed to someone without Purchases.Create', async () => {
    const router = open('/purchases/new', ...VIEW)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('without Purchases.View the list bounces to the forbidden page', async () => {
    const router = open('/purchases', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('filters by the supplier in the URL and says so', async () => {
    open('/purchases?supplier=1', ...ALL)

    expect(await screen.findByText('Supplier: Acme')).toBeInTheDocument()
    await waitFor(() => expect(api.calls.some((c) => c.url.includes('/purchases?') && c.url.includes('supplierId=1'))).toBe(true))
  })
})

describe('new purchase', () => {
  const openForm = async () => {
    open('/purchases/new?supplier=1', ...ALL)
    return screen.findByRole('heading', { name: 'New purchase' })
  }

  it('refuses to save without products and does not call the API', async () => {
    await openForm()

    fireEvent.click(screen.getByRole('button', { name: 'Save purchase' }))

    expect(await screen.findByText('Add at least one product.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('adds a product, uses the supplier discount, and sends exactly what was typed', async () => {
    await openForm()
    api.routes['POST /api/v1/purchases'] = () => json(detail({ id: 7, number: 'PUR-000007' }), 201)
    api.routes['GET /api/v1/purchases/7'] = () => json(detail({ id: 7, number: 'PUR-000007' }))

    const picker = await screen.findByRole('combobox', { name: /Add a product/ })
    fireEvent.change(picker, { target: { value: 'olive' } })
    fireEvent.click(await screen.findByRole('option', { name: /Olive oil 1L/ }))

    // The product's own cost (same currency as the supplier) is the starting unit cost.
    const cost = await screen.findByLabelText('Unit cost: Olive oil 1L')
    expect(cost).toHaveValue('8')
    fireEvent.change(screen.getByLabelText('Quantity: Olive oil 1L'), { target: { value: '5' } })
    fireEvent.change(cost, { target: { value: '9' } })

    // 5 x 9 = 45, minus the supplier's 10% = 40.50; paying in full leaves nothing to owe.
    fireEvent.click(screen.getByRole('button', { name: 'Pay in full' }))
    expect(screen.getAllByText(/40\.50/).length).toBeGreaterThan(0)

    fireEvent.click(screen.getByRole('button', { name: 'Save purchase' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({
      supplierId: 1,
      purchaseDate: expect.stringMatching(/^\d{4}-\d{2}-\d{2}$/),
      supplierReference: null,
      notes: null,
      discountPercent: 10,
      amountPaid: 40.5,
      payFromDrawer: true, // the user's drawer is open, so paid cash comes out of it unless they untick it
      lines: [{ productId: 9, quantity: 5, unitCost: 9, updateCostPrice: true }],
    }))
    await waitFor(() => expect(api.calls.some((c) => c.url.endsWith('/purchases/7'))).toBe(true))
  })

  it("offers only the chosen supplier's products", async () => {
    await openForm()

    fireEvent.change(await screen.findByRole('combobox', { name: /Add a product/ }), { target: { value: 'olive' } })
    await screen.findByRole('option', { name: /Olive oil 1L/ })

    const productCall = api.calls.find((c) => c.url.startsWith('/api/v1/products?'))
    expect(new URL(productCall!.url, 'http://x').searchParams.get('supplierId')).toBe('1')
  })

  it('loads products lazily: a page when the list opens, the next one only when it is scrolled', async () => {
    await openForm()
    const many = (from: number, to: number) => Array.from({ length: to - from + 1 }, (_, i) => ({ ...product, id: 100 + from + i, name: `Product ${from + i}`, barcode: `B${from + i}` }))
    api.routes['GET /api/v1/products'] = (_init, url) => {
      const pageNumber = Number(new URL(url, 'http://x').searchParams.get('page'))
      return json({ items: pageNumber === 1 ? many(1, 20) : many(21, 25), total: 25, page: pageNumber, pageSize: 20 })
    }
    const productPages = () => api.calls.filter((c) => c.url.startsWith('/api/v1/products?')).length
    expect(productPages()).toBe(0) // nothing is requested until the picker is used

    fireEvent.click(await screen.findByRole('combobox', { name: /Add a product/ }))
    await screen.findByRole('option', { name: /^Product 1B1/ })
    expect(screen.getAllByRole('option')).toHaveLength(20)
    expect(productPages()).toBe(1)

    fireEvent.scroll(screen.getByRole('listbox'))

    expect(await screen.findByRole('option', { name: /^Product 25B25/ })).toBeInTheDocument()
    expect(screen.getAllByRole('option')).toHaveLength(25)
    expect(productPages()).toBe(2)
    fireEvent.scroll(screen.getByRole('listbox')) // nothing left: no third request
    await new Promise((r) => setTimeout(r, 50))
    expect(productPages()).toBe(2)
  })

  it('opens the new-product window for this supplier, and only for someone who may create products', async () => {
    open('/purchases/new?supplier=1', ...ALL, Permissions.Products_Create)
    await screen.findByRole('heading', { name: 'New purchase' })
    const opener = vi.spyOn(window, 'open').mockReturnValue(null)

    fireEvent.click(await screen.findByRole('button', { name: 'Add new product' }))

    expect(opener).toHaveBeenCalledWith(expect.stringContaining('/products/new?supplier=1'), '_blank')
    opener.mockRestore()
  })

  it('shows no new-product button without Products.Create', async () => {
    await openForm()
    await screen.findByRole('combobox', { name: /Add a product/ })

    expect(screen.queryByRole('button', { name: 'Add new product' })).not.toBeInTheDocument()
  })

  const pickProductAndPay = async (paidText: string) => {
    fireEvent.change(await screen.findByRole('combobox', { name: /Add a product/ }), { target: { value: 'olive' } })
    fireEvent.click(await screen.findByRole('option', { name: /Olive oil 1L/ }))
    await screen.findByLabelText('Unit cost: Olive oil 1L')
    fireEvent.change(screen.getByLabelText('Paid now'), { target: { value: paidText } })
  }

  it('takes the payment from the open cash drawer by default, and sends false once it is unticked', async () => {
    await openForm()
    api.routes['POST /api/v1/purchases'] = () => json(detail({ id: 7, number: 'PUR-000007' }), 201)
    api.routes['GET /api/v1/purchases/7'] = () => json(detail({ id: 7, number: 'PUR-000007' }))
    await pickProductAndPay('2')

    const box = await screen.findByRole('checkbox', { name: 'Take the payment from the cash drawer' })
    expect(box).toBeChecked()
    fireEvent.click(box)
    fireEvent.click(screen.getByRole('button', { name: 'Save purchase' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toMatchObject({ amountPaid: 2, payFromDrawer: false }))
  })

  it('offers no drawer checkbox while nothing is paid, and cannot use a drawer that is closed', async () => {
    openAs('/purchases/new?supplier=1', makeSession({ permissions: [...ALL], drawer: { isOpen: false, openingCash: 0 } }))
    await screen.findByRole('heading', { name: 'New purchase' })
    api.routes['POST /api/v1/purchases'] = () => json(detail({ id: 7, number: 'PUR-000007' }), 201)
    api.routes['GET /api/v1/purchases/7'] = () => json(detail({ id: 7, number: 'PUR-000007' }))

    expect(screen.queryByRole('checkbox', { name: 'Take the payment from the cash drawer' })).not.toBeInTheDocument()
    await pickProductAndPay('2')
    const box = await screen.findByRole('checkbox', { name: 'Take the payment from the cash drawer' })
    expect(box).toBeDisabled()
    expect(box).not.toBeChecked()
    expect(screen.getByText(/drawer is not open/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Save purchase' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toMatchObject({ amountPaid: 2, payFromDrawer: false }))
  })

  it('shows the server message and keeps the form when a rule fails', async () => {
    await openForm()
    api.routes['POST /api/v1/purchases'] = () => problem(400, 'paid_exceeds_total', 'The amount paid is more than the purchase total.')

    const picker = await screen.findByRole('combobox', { name: /Add a product/ })
    fireEvent.change(picker, { target: { value: 'olive' } })
    fireEvent.click(await screen.findByRole('option', { name: /Olive oil 1L/ }))
    await screen.findByLabelText('Unit cost: Olive oil 1L')
    fireEvent.click(screen.getByRole('button', { name: 'Save purchase' }))

    expect(await screen.findByText('The amount paid is more than the purchase total.')).toBeInTheDocument()
    expect(screen.getByLabelText('Unit cost: Olive oil 1L')).toBeInTheDocument()
  })
})

describe('purchase details', () => {
  it('shows the note, and offers print, PDF and cancel to someone who may', async () => {
    open('/purchases/3', ...ALL)

    expect(await screen.findByRole('heading', { name: 'PUR-000003' })).toBeInTheDocument()
    expect(screen.getByText('Olive oil 1L')).toBeInTheDocument()
    expect(screen.getByText('Second delivery')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Print receipt' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Download PDF' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel purchase' })).toBeInTheDocument()
  })

  it('hides Cancel without Purchases.Cancel', async () => {
    open('/purchases/3', ...VIEW)
    await screen.findByRole('heading', { name: 'PUR-000003' })

    expect(screen.queryByRole('button', { name: 'Cancel purchase' })).not.toBeInTheDocument()
  })

  it('cancels with a reason and shows the cancelled banner afterwards', async () => {
    open('/purchases/3', ...ALL)
    api.routes['POST /api/v1/purchases/3/cancel'] = () =>
      json(detail({ status: 'cancelled', cancelledAt: '2026-11-04T09:00:00Z', cancelledBy: 'Sara Admin', cancelReason: 'Wrong supplier' }))
    await screen.findByRole('heading', { name: 'PUR-000003' })

    fireEvent.click(screen.getByRole('button', { name: 'Cancel purchase' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText('Reason (optional)'), { target: { value: 'Wrong supplier' } })
    api.routes['GET /api/v1/purchases/3'] = () => json(detail({ status: 'cancelled', cancelledAt: '2026-11-04T09:00:00Z', cancelledBy: 'Sara Admin', cancelReason: 'Wrong supplier' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel this purchase' }))

    await waitFor(() => expect(api.calls.find((c) => c.url.endsWith('/purchases/3/cancel'))?.body).toEqual({ reason: 'Wrong supplier' }))
    expect(await screen.findByText(/Cancelled on/)).toBeInTheDocument()
  })

  it('shows the server refusal when part of the stock was already sold', async () => {
    open('/purchases/3', ...ALL)
    api.routes['POST /api/v1/purchases/3/cancel'] = () => problem(409, 'stock_already_sold', 'Part of Olive oil 1L from this purchase has already been sold.')
    await screen.findByRole('heading', { name: 'PUR-000003' })

    fireEvent.click(screen.getByRole('button', { name: 'Cancel purchase' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel this purchase' }))

    expect(await within(dialog).findByText(/has already been sold/)).toBeInTheDocument()
    expect(screen.queryByText(/Cancelled on/)).not.toBeInTheDocument()
  })
})
