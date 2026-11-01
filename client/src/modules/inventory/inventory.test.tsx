import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const item = (id: number, name: string, overrides = {}) => ({
  id,
  name,
  barcode: `BC${id}`,
  categoryName: 'Drinks',
  currentStock: 10,
  minimumStock: 5,
  maximumStock: 50,
  costPrice: 2,
  sellingPrice: 3,
  currencyNotation: '$',
  stockValue: 20,
  unit: 'piece',
  isActive: true,
  status: 'ok',
  ...overrides,
})

const page = <T,>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 25 })
const summary = { totalProducts: 3, lowStockCount: 2, outOfStockCount: 1, totalStockValue: 1234.5, currencyNotation: '$' }

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'inventory', 'print')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

const VIEW = [Permissions.Inventory_View]
const EDIT = [Permissions.Inventory_View, Permissions.Inventory_Edit]

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/inventory'] = () =>
    json(page([item(1, 'Cola', { status: 'ok' }), item(2, 'Chips', { currentStock: 3, status: 'low' }), item(3, 'Water', { currentStock: 0, status: 'out' })]))
  api.routes['GET /api/v1/inventory/summary'] = () => json(summary)
  return mountModule(path, routes)
}

describe('inventory list', () => {
  it('shows the status the server decided and the four tiles', async () => {
    open('/inventory', ...VIEW)

    expect(await screen.findByText('Cola')).toBeInTheDocument()
    const table = screen.getByRole('table')
    expect(within(table).getByText('In Stock')).toBeInTheDocument()
    expect(within(table).getByText('Low Stock')).toBeInTheDocument()
    expect(within(table).getByText('Out of Stock')).toBeInTheDocument()
    expect(await screen.findByText(/1,234.50/)).toBeInTheDocument()
    expect(screen.getByText('Total Stock Value')).toBeInTheDocument()
  })

  it('asks the API for the sort the user clicked, and keeps it in the URL', async () => {
    const router = open('/inventory', ...VIEW)
    await screen.findByText('Cola')

    fireEvent.click(screen.getByRole('button', { name: /Current Stock/ }))

    await waitFor(() => expect(router.state.location.search).toBe('?sort=stock&dir=asc'))
    await waitFor(() => expect(api.calls.some((c) => c.url.includes('sort=stock') && c.url.includes('dir=asc'))).toBe(true))
  })

  it('a viewer sees no adjust or minimum buttons', async () => {
    open('/inventory', ...VIEW)
    await screen.findByText('Cola')

    expect(screen.queryByRole('button', { name: 'Adjust Stock' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Set Minimum Stock' })).not.toBeInTheDocument()
  })

  it('without Inventory.View the route bounces to the forbidden page', async () => {
    const router = open('/inventory', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('adjust stock', () => {
  const openAdjust = async () => {
    open('/inventory', ...EDIT)
    await screen.findByText('Cola')
    fireEvent.click(screen.getAllByRole('button', { name: 'Adjust Stock' })[0]!)
    return screen.findByRole('dialog', { name: 'Adjust Stock' })
  }

  it('previews the new level and posts an add with the reason', async () => {
    const dialog = await openAdjust()
    api.routes['POST /api/v1/inventory/1/adjust'] = () => json({ previousStock: 10, newStock: 14.5, item: item(1, 'Cola', { currentStock: 14.5 }) })

    fireEvent.change(within(dialog).getByLabelText(/Quantity/), { target: { value: '4.5' } })
    expect(within(dialog).getByRole('status')).toHaveTextContent('14.5')
    fireEvent.change(within(dialog).getByLabelText('Reason for Adjustment'), { target: { value: ' delivery ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ type: 'add', quantity: 4.5, reason: 'delivery' }))
    expect(await screen.findByText('Stock adjusted. New level: 14.5')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('will not remove more than is in stock and never calls the API', async () => {
    const dialog = await openAdjust()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Remove Stock' }))
    fireEvent.change(within(dialog).getByLabelText(/Quantity/), { target: { value: '11' } })

    expect(within(dialog).getByRole('status')).toHaveTextContent('Only 10 in stock')
    expect(within(dialog).getByRole('button', { name: 'Save' })).toBeDisabled()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('refuses a missing quantity without calling the API', async () => {
    const dialog = await openAdjust()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Enter a quantity greater than zero.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('shows the server error on the quantity field and keeps the dialog open', async () => {
    const dialog = await openAdjust()
    api.routes['POST /api/v1/inventory/1/adjust'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { quantity: ['Only 10 in stock'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })

    fireEvent.change(within(dialog).getByLabelText(/Quantity/), { target: { value: '2' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Only 10 in stock')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Adjust Stock' })).toBeInTheDocument()
  })
})

describe('minimum stock', () => {
  it('puts the current minimum in the box and saves the new one', async () => {
    open('/inventory', ...EDIT)
    api.routes['PUT /api/v1/inventory/1/minimum-stock'] = () => json(item(1, 'Cola', { minimumStock: 8 }))
    await screen.findByText('Cola')

    fireEvent.click(screen.getAllByRole('button', { name: 'Set Minimum Stock' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Set Minimum Stock' })
    const box = within(dialog).getByLabelText(/Minimum Stock Level/)
    expect(box).toHaveValue('5')
    fireEvent.change(box, { target: { value: '8' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'PUT')?.body).toEqual({ minimumStock: 8 }))
    expect(await screen.findByText('Minimum stock level updated successfully')).toBeInTheDocument()
  })
})

describe('low stock report', () => {
  const report = {
    summary: { products: 2, outOfStock: 1, totalUnits: 3 },
    items: page([
      { id: 3, name: 'Water', description: 'Still', barcode: 'BC3', categoryName: null, currentStock: 0, minimumStock: 10, deficit: 10, unit: 'piece', priority: 'critical' },
      { id: 2, name: 'Chips', description: null, barcode: 'BC2', categoryName: 'Snacks', currentStock: 3, minimumStock: 10, deficit: 7, unit: null, priority: 'high' },
    ]),
  }

  it('lists the products with deficit and priority, from the server', async () => {
    open('/inventory/low-stock', ...EDIT)
    api.routes['GET /api/v1/inventory/reports/low-stock'] = () => json(report)

    expect(await screen.findByText('Water')).toBeInTheDocument()
    const table = screen.getByRole('table')
    expect(within(table).getByText('Critical')).toBeInTheDocument()
    expect(within(table).getByText('High')).toBeInTheDocument()
    expect(within(table).getByText('No Category')).toBeInTheDocument()
    expect(screen.getByText('Products with Low Stock')).toBeInTheDocument()
  })

  it('shows the printer message and a PDF fallback when the receipt printer is unavailable', async () => {
    open('/inventory/low-stock', ...EDIT)
    api.routes['GET /api/v1/inventory/reports/low-stock'] = () => json(report)
    api.routes['POST /api/v1/inventory/reports/low-stock/print'] = () => json({ printed: false, message: 'Printer is not connected', pdfUrl: '/api/v1/inventory/reports/low-stock.pdf' })
    await screen.findByText('Water')

    fireEvent.click(screen.getByRole('button', { name: 'Print on receipt printer' }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent('Printer is not connected')
    expect(within(alert).getByRole('button', { name: 'Download PDF instead' })).toBeInTheDocument()
  })

  it('toasts when the printer accepted the job', async () => {
    open('/inventory/low-stock', ...EDIT)
    api.routes['GET /api/v1/inventory/reports/low-stock'] = () => json(report)
    api.routes['POST /api/v1/inventory/reports/low-stock/print'] = () => json({ printed: true, message: 'ok', pdfUrl: '/x.pdf' })
    await screen.findByText('Water')

    fireEvent.click(screen.getByRole('button', { name: 'Print on receipt printer' }))

    expect(await screen.findByText('Sent to the receipt printer')).toBeInTheDocument()
  })

  it('shows the all-clear when nothing is low', async () => {
    open('/inventory/low-stock', ...EDIT)
    api.routes['GET /api/v1/inventory/reports/low-stock'] = () => json({ summary: { products: 0, outOfStock: 0, totalUnits: 0 }, items: page([]) })

    expect(await screen.findByText('Great News!')).toBeInTheDocument()
  })
})

describe('stock value report', () => {
  it('shows the totals, both ranked lists and the share of each product', async () => {
    open('/inventory/stock-value', ...VIEW)
    api.routes['GET /api/v1/inventory/reports/stock-value'] = () =>
      json({
        totalProducts: 2,
        totalUnits: 15,
        totalValue: 300,
        averageValue: 150,
        currencyNotation: '$',
        byCategory: [{ label: 'Drinks', value: 200 }, { label: '', value: 100 }],
        topProducts: [{ label: 'Cola', value: 200 }],
        items: page([
          { id: 1, name: 'Cola', categoryName: 'Drinks', stockQuantity: 10, unit: 'piece', unitCost: 20, stockValue: 200, currencyNotation: '$', valuePercent: 66.7 },
          { id: 2, name: 'Chips', categoryName: null, stockQuantity: 5, unit: null, unitCost: 20, stockValue: 100, currencyNotation: '$', valuePercent: 33.3 },
        ]),
      })

    expect(await screen.findByRole('table')).toBeInTheDocument()
    expect(await screen.findByText(/66.7%/)).toBeInTheDocument()
    expect(screen.getByText(/300.00/, { selector: '.tabular-nums' })).toBeInTheDocument()
    // The category with an empty label is the "no category" bucket.
    const byCategory = screen.getByRole('region', { name: 'Stock Value by Category' })
    expect(within(byCategory).getByText('No Category')).toBeInTheDocument()
    expect(within(byCategory).getByText(/200.00/)).toBeInTheDocument()
  })

  it('surfaces an API failure with a retry', async () => {
    open('/inventory/stock-value', ...VIEW)
    api.routes['GET /api/v1/inventory/reports/stock-value'] = () => problem(400, 'bad_request', 'It broke')

    expect(await screen.findByText('It broke')).toBeInTheDocument()
  })
})
