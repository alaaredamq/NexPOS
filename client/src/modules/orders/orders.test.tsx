import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { readListParams, writeListParams } from './listParams'
import routes from './routes'

const order = (id: number, overrides = {}) => ({
  id,
  orderNumber: `ORD-${String(id).padStart(6, '0')}`,
  customerId: 9,
  customerName: 'Elias Sfeir',
  orderDate: '2026-10-31T10:00:00Z',
  totalAmount: 12.5,
  currencyId: 1,
  currencyNotation: '$',
  status: 3,
  itemCount: 2,
  ...overrides,
})

const page = (items: ReturnType<typeof order>[]) => ({ items, total: items.length, page: 1, pageSize: 25 })
const summary = { periodDays: 30, totalOrders: 7, deliveredOrders: 5, pendingOrders: 1, processingOrders: 0, cancelledOrders: 1, revenue: [{ currencyId: 1, currencyNotation: '$', amount: 120 }, { currencyId: 3, currencyNotation: 'L.L.', amount: 3000000 }] }

const detail = {
  id: 42,
  orderNumber: 'ORD-000042',
  customerId: 9,
  customerName: 'Elias Sfeir',
  orderDate: '2026-10-31T10:00:00Z',
  createdAt: '2026-10-31T10:00:00Z',
  updatedAt: null,
  status: 3,
  currencyId: 1,
  currencyCode: 'USD',
  currencyNotation: '$',
  subtotal: 20,
  discountAmount: 2,
  discountPercentage: 10,
  adjustment: 0.5,
  taxAmount: 0,
  totalAmount: 18.5,
  lines: [{ productId: 5, productName: 'Batteries AA', barcode: '12345', quantity: 4, weightOrVolume: null, unitPrice: 5, totalPrice: 20 }],
  receipts: [{ id: 7, receiptNumber: 'RCP-20261031-001', isVoid: false }],
}

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'orders', 'print')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/orders'] = () => json(page([order(42), order(43, { customerId: null, customerName: null, status: 4, currencyNotation: 'L.L.', totalAmount: 900000 })]))
  api.routes['GET /api/v1/orders/summary'] = () => json(summary)
  api.routes['GET /api/v1/orders/42'] = () => json(detail)
  return mountModule(path, routes)
}

const ADMIN = [Permissions.Orders_View, Permissions.Orders_Edit]

describe('url state', () => {
  it('round-trips the filters and leaves the defaults out of the URL', () => {
    const params = readListParams(new URLSearchParams('q=elias&status=3&from=2026-10-01&to=2026-10-31&sort=total&dir=desc&page=2&size=50&customer=9'))
    expect(params).toMatchObject({ q: 'elias', status: '3', from: '2026-10-01', to: '2026-10-31', sort: 'total', dir: 'desc', page: 2, pageSize: 50, customerId: 9 })
    expect(writeListParams(params).toString()).toBe(new URLSearchParams('q=elias&page=2&size=50&sort=total&dir=desc&status=3&from=2026-10-01&to=2026-10-31&customer=9').toString())
    expect(writeListParams(readListParams(new URLSearchParams())).toString()).toBe('')
  })

  it('ignores values it does not know', () => {
    const params = readListParams(new URLSearchParams('status=9&sort=password&from=yesterday&page=-3&size=7'))
    expect(params).toMatchObject({ status: '', sort: 'date', dir: 'desc', from: '', page: 1, pageSize: 25 })
  })
})

describe('orders list', () => {
  it('shows the rows with their currency, status and a walk-in label, and the last-30-days tiles per currency', async () => {
    open('/orders', ...ADMIN)

    expect(await screen.findByText('ORD-000042')).toBeInTheDocument()
    expect(screen.getByText('Elias Sfeir')).toBeInTheDocument()
    expect(screen.getByText('Walk-in customer')).toBeInTheDocument()
    expect(screen.getAllByText('Delivered').length).toBeGreaterThan(0)
    expect(await screen.findByText(/\$ 120\.00.*L\.L\. 3,000,000\.00/)).toBeInTheDocument() // never added together
    expect(screen.getAllByText('Last 30 days').length).toBe(4)
  })

  it('asks the API for the sort the user clicked and keeps it in the URL', async () => {
    const router = open('/orders', ...ADMIN)
    await screen.findByText('ORD-000042')

    fireEvent.click(screen.getByRole('button', { name: /Total/ }))

    await waitFor(() => expect(router.state.location.search).toBe('?sort=total&dir=asc'))
    await waitFor(() => expect(api.calls.some((c) => c.url.includes('sort=total') && c.url.includes('dir=asc'))).toBe(true))
  })

  it('a status chip filters on the server and goes back to page 1', async () => {
    const router = open('/orders?page=3', ...ADMIN)
    await screen.findByText('ORD-000042')

    fireEvent.click(screen.getByRole('button', { name: 'Cancelled' }))

    await waitFor(() => expect(router.state.location.search).toBe('?status=4'))
    await waitFor(() => expect(api.calls.some((c) => c.url.includes('status=4') && !c.url.includes('page=3'))).toBe(true))
  })

  it('a cashier who may only view sees no status button', async () => {
    open('/orders', Permissions.Orders_View)

    await screen.findByText('ORD-000042')

    expect(screen.queryByRole('button', { name: 'Update Status' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Print Receipt' }).length).toBeGreaterThan(0)
  })

  it('without Orders.View the route bounces to the forbidden page', async () => {
    const router = open('/orders', Permissions.Products_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('a reprint asks the till endpoint and says so', async () => {
    open('/orders', ...ADMIN)
    api.routes['POST /api/v1/pos/sales/42/print'] = () => json({ printed: true, message: 'Sent to the receipt printer', pdfUrl: '/x' })
    await screen.findByText('ORD-000042')

    fireEvent.click(screen.getAllByRole('button', { name: 'Print Receipt' })[0]!)

    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url === '/api/v1/pos/sales/42/print')).toBe(true))
    expect(await screen.findByText('Sent to the receipt printer')).toBeInTheDocument()
  })

  it('offers the PDF when the printer cannot be reached', async () => {
    open('/orders', ...ADMIN)
    api.routes['POST /api/v1/pos/sales/42/print'] = () => json({ printed: false, message: 'Could not print.', pdfUrl: '/x' })
    await screen.findByText('ORD-000042')

    fireEvent.click(screen.getAllByRole('button', { name: 'Print Receipt' })[0]!)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Could not print.')).toBeInTheDocument()
    expect(within(dialog).getByRole('button', { name: 'Download PDF instead' })).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' })) // the confirm store is global: do not leak the open dialog into the next test
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})

describe('status dialog', () => {
  const openDialog = async () => {
    open('/orders', ...ADMIN)
    await screen.findByText('ORD-000042')
    fireEvent.click(screen.getAllByRole('button', { name: 'Update Status' })[0]!)
    return await screen.findByRole('dialog', { name: 'Update Order Status' })
  }

  const choose = async (dialog: HTMLElement, name: string) => {
    fireEvent.click(within(dialog).getByRole('combobox'))
    fireEvent.click(await screen.findByRole('option', { name }))
  }

  it('cannot save until another status is chosen', async () => {
    const dialog = await openDialog()

    expect(within(dialog).getByRole('button', { name: 'Update Status' })).toBeDisabled()
  })

  it('a plain change puts the number and says it was updated', async () => {
    const dialog = await openDialog()
    api.routes['PUT /api/v1/orders/42/status'] = () => json({ orderId: 42, orderNumber: 'ORD-000042', status: 2 })

    await choose(dialog, 'Shipped')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Update Status' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'PUT')?.body).toEqual({ status: 2 }))
    expect(await screen.findByText('Order status updated successfully')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('Cancelled warns that the whole sale is reversed and reports what the reversal did', async () => {
    const dialog = await openDialog()
    api.routes['PUT /api/v1/orders/42/status'] = () =>
      json({ orderId: 42, orderNumber: 'ORD-000042', status: 4, reversal: { orderCancelled: true, receiptsVoided: 1, stockLinesRestored: 2, debtReversed: 0 } })

    await choose(dialog, 'Cancelled')
    expect(within(dialog).getByRole('alert')).toHaveTextContent(/reverses the whole sale/)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel order' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'PUT')?.body).toEqual({ status: 4 }))
    expect(await screen.findByText('Order ORD-000042 cancelled: receipt voided and 2 item line(s) returned to stock.')).toBeInTheDocument()
  })

  it('Returned says it is only a label', async () => {
    const dialog = await openDialog()

    await choose(dialog, 'Returned')

    expect(within(dialog).getByText(/Only changes the label/)).toBeInTheDocument()
  })

  it('shows the server\'s refusal in the dialog and keeps it open', async () => {
    const dialog = await openDialog()
    api.routes['PUT /api/v1/orders/42/status'] = () => problem(409, 'order_closed', 'Order ORD-000042 has already been cancelled or returned.')

    await choose(dialog, 'Cancelled')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel order' }))

    expect(await within(dialog).findByText(/already been cancelled or returned/)).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Update Order Status' })).toBeInTheDocument()
  })
})

describe('order details', () => {
  it('shows lines, the discount, the cash-rounding adjustment, the total and the receipt link', async () => {
    open('/orders/42', ...ADMIN)

    expect(await screen.findByRole('heading', { name: 'ORD-000042' })).toBeInTheDocument()
    expect(screen.getByText('Batteries AA')).toBeInTheDocument()
    expect(screen.getByText('12345')).toBeInTheDocument()
    expect(screen.getByText(/Discount \(.*10%.*\)/)).toBeInTheDocument()
    expect(screen.getByText('Surcharge')).toBeInTheDocument()
    expect(screen.getAllByText(/18\.50/).length).toBeGreaterThan(0)
    expect(screen.getByRole('link', { name: 'RCP-20261031-001' })).toHaveAttribute('href', '/receipts/7')
  })

  it('offers to change the customer only to someone holding the change-customer permission', async () => {
    // Editing orders is not enough: the permission is separate because it moves money between accounts.
    open('/orders/42', ...ADMIN)
    await screen.findByRole('heading', { name: 'ORD-000042' })
    expect(screen.queryByRole('button', { name: 'Change customer' })).not.toBeInTheDocument()

    open('/orders/42', ...ADMIN, Permissions.Sales_ChangeCustomer)
    expect(await screen.findByRole('button', { name: 'Change customer' })).toBeInTheDocument()
  })

  it('shows the not-found card for an order that does not exist', async () => {
    open('/orders/99', ...ADMIN)
    api.routes['GET /api/v1/orders/99'] = () => problem(404, 'order_not_found')

    expect(await screen.findByText('That order was not found.')).toBeInTheDocument()
  })

  it('a viewer sees no status button on the details either', async () => {
    open('/orders/42', Permissions.Orders_View)

    await screen.findByRole('heading', { name: 'ORD-000042' })

    expect(screen.queryByRole('button', { name: 'Update Status' })).not.toBeInTheDocument()
  })
})
