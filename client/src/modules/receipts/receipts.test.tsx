import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { readListParams, writeListParams } from './listParams'
import routes from './routes'

const receipt = (id: number, overrides = {}) => ({
  id,
  receiptNumber: `RCP-20261031-00${id}`,
  orderId: 40 + id,
  receiptDate: '2026-10-31T10:00:00Z',
  customerName: 'Elias Sfeir',
  cashierName: 'Layla Admin',
  paymentMethod: 'Cash',
  totalAmount: 12.5,
  currencyNotation: '$',
  isVoid: false,
  voidDate: null,
  voidReason: null,
  voidedBy: null,
  ...overrides,
})

const voided = receipt(2, { isVoid: true, voidDate: '2026-10-31T11:00:00Z', voidReason: 'Wrong item', voidedBy: 'Layla Admin', customerName: null })
const page = (items: ReturnType<typeof receipt>[]) => ({ items, total: items.length, page: 1, pageSize: 25 })

const stats = {
  from: '2026-10-02',
  to: '2026-11-01',
  validCount: 3,
  voidCount: 1,
  currencies: [
    { currencyId: 1, currencyNotation: '$', totalSales: 100, receiptCount: 2, averageReceipt: 50, daily: [{ date: '2026-10-30', amount: 40 }, { date: '2026-10-31', amount: 60 }] },
    { currencyId: 3, currencyNotation: 'L.L.', totalSales: 900000, receiptCount: 1, averageReceipt: 900000, daily: [{ date: '2026-10-31', amount: 900000 }] },
  ],
  paymentMethods: [{ paymentMethod: 'Cash', count: 2 }, { paymentMethod: 'Card', count: 1 }],
}

const detail = {
  id: 1,
  receiptNumber: 'RCP-20261031-001',
  orderId: 41,
  receiptDate: '2026-10-31T10:00:00Z',
  customerId: 9,
  customerName: 'Elias Sfeir',
  customerPhone: '70123456',
  cashierName: 'Layla Admin',
  paymentMethod: 'Cash',
  currencyCode: 'USD',
  currencyNotation: '$',
  exchangeRate: 1,
  subTotal: 20,
  discountAmount: 2,
  discountPercentage: 10,
  taxAmount: 0,
  adjustment: 0,
  totalAmount: 18,
  amountPaid: 20,
  changeAmount: 2,
  isFullyPaid: true,
  notes: null,
  isVoid: false,
  voidDate: null,
  voidReason: null,
  voidedBy: null,
  createdAt: '2026-10-31T10:00:00Z',
  printedBy: null,
  printedAt: null,
  lines: [{ productName: 'Batteries AA', barcode: '12345', quantity: 4, unit: 'piece', unitPrice: 5, discountAmount: 0, taxAmount: 0, lineTotal: 20 }],
  payments: [{ paymentMethod: 'Cash', referenceNumber: null, amount: 20, paymentDate: '2026-10-31T10:00:00Z' }],
}

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'receipts', 'print')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/receipts'] = (_init, url) => json(page(url.includes('status=void') ? [voided] : [receipt(1), voided]))
  api.routes['GET /api/v1/receipts/stats'] = () => json(stats)
  api.routes['GET /api/v1/receipts/1'] = () => json(detail)
  return mountModule(path, routes)
}

const ADMIN = [Permissions.Sales_View, Permissions.Sales_Edit]

describe('url state', () => {
  it('round-trips the filters and leaves the defaults out of the URL', () => {
    const params = readListParams(new URLSearchParams('q=rcp&status=void&from=2026-10-01&to=2026-10-31&sort=amount&dir=desc&page=2&size=50&view=analytics'))
    expect(params).toMatchObject({ q: 'rcp', status: 'void', from: '2026-10-01', to: '2026-10-31', sort: 'amount', dir: 'desc', page: 2, pageSize: 50, view: 'analytics' })
    expect(writeListParams(params).toString()).toBe(new URLSearchParams('q=rcp&page=2&size=50&sort=amount&dir=desc&status=void&from=2026-10-01&to=2026-10-31&view=analytics').toString())
    expect(writeListParams(readListParams(new URLSearchParams())).toString()).toBe('')
  })

  it('ignores values it does not know', () => {
    expect(readListParams(new URLSearchParams('status=maybe&sort=password&view=x&from=yesterday'))).toMatchObject({ status: 'all', sort: 'date', dir: 'desc', view: 'list', from: '' })
  })
})

describe('receipts list', () => {
  it('shows the rows, a void badge and the tiles totalled per currency', async () => {
    open('/receipts', ...ADMIN)

    expect(await screen.findByText('RCP-20261031-001')).toBeInTheDocument()
    expect(screen.getByText('Walk-in Customer')).toBeInTheDocument()
    expect(screen.getAllByText('Void').length).toBeGreaterThan(0)
    expect(await screen.findByText(/\$ 100\.00.*L\.L\. 900,000\.00/)).toBeInTheDocument() // never added together
    expect(screen.getByText(/\$ 50\.00.*L\.L\. 900,000\.00/)).toBeInTheDocument()
  })

  it('the Void filter asks for void receipts and swaps in the reason columns', async () => {
    const router = open('/receipts', ...ADMIN)
    await screen.findByText('RCP-20261031-001')

    fireEvent.click(screen.getByRole('button', { name: 'Void Receipts' }))

    await waitFor(() => expect(router.state.location.search).toBe('?status=void'))
    expect(await screen.findByText('Wrong item')).toBeInTheDocument()
    expect(screen.getByRole('columnheader', { name: 'Void Date' })).toBeInTheDocument()
    expect(screen.queryByText('RCP-20261031-001')).not.toBeInTheDocument()
  })

  it('a viewer sees no void button', async () => {
    open('/receipts', Permissions.Sales_View)

    await screen.findByText('RCP-20261031-001')

    expect(screen.queryByRole('button', { name: 'Void Receipt' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Print Receipt' }).length).toBeGreaterThan(0)
  })

  it('the cashier\'s Orders.View alone does not open the receipts', async () => {
    const router = open('/receipts', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('the analytics view shows the payment methods and the daily sales, with a currency switch', async () => {
    open('/receipts?view=analytics', ...ADMIN)

    expect(await screen.findByText('Daily Sales Trend')).toBeInTheDocument()
    expect(screen.getByText('Card')).toBeInTheDocument()
    expect(screen.getByText(/best day/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'L.L.' }))
    expect(await screen.findByText(/best day.*900,000\.00/)).toBeInTheDocument()
  })

  it('a reprint goes to the receipt endpoint', async () => {
    open('/receipts', ...ADMIN)
    api.routes['POST /api/v1/receipts/1/print'] = () => json({ printed: true, message: 'Sent to the receipt printer', pdfUrl: '/x' })
    await screen.findByText('RCP-20261031-001')

    fireEvent.click(screen.getAllByRole('button', { name: 'Print Receipt' })[0]!)

    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url === '/api/v1/receipts/1/print')).toBe(true))
    expect(await screen.findByText('Sent to the receipt printer')).toBeInTheDocument()
  })
})

describe('voiding', () => {
  const openDialog = async () => {
    open('/receipts', ...ADMIN)
    await screen.findByText('RCP-20261031-001')
    fireEvent.click(screen.getAllByRole('button', { name: 'Void Receipt' })[0]!)
    return await screen.findByRole('dialog')
  }

  it('will not void without a reason and does not call the API', async () => {
    const dialog = await openDialog()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Void Receipt' }))

    expect(await within(dialog).findByText('Please enter a reason for voiding')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('sends the trimmed reason, then says the sale was reversed and refreshes the list', async () => {
    const dialog = await openDialog()
    api.routes['POST /api/v1/receipts/1/void'] = () => json({ orderCancelled: true, receiptsVoided: 1, stockLinesRestored: 1, debtReversed: 0 })

    fireEvent.change(within(dialog).getByLabelText(/Reason for Voiding/), { target: { value: '  Wrong item  ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Void Receipt' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ reason: 'Wrong item' }))
    expect(await screen.findByText('Receipt voided, order cancelled and items returned to stock')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('shows the server\'s refusal in the dialog and keeps it open', async () => {
    const dialog = await openDialog()
    api.routes['POST /api/v1/receipts/1/void'] = () => problem(409, 'receipt_already_void', 'Receipt RCP-20261031-001 is already void.')

    fireEvent.change(within(dialog).getByLabelText(/Reason for Voiding/), { target: { value: 'again' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Void Receipt' }))

    expect(await within(dialog).findByText('Receipt RCP-20261031-001 is already void.')).toBeInTheDocument()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })
})

describe('receipt details', () => {
  it('shows the lines, the discount, the payments and the order link', async () => {
    open('/receipts/1', ...ADMIN)

    expect(await screen.findByRole('heading', { name: 'RCP-20261031-001' })).toBeInTheDocument()
    expect(screen.getByText('Batteries AA')).toBeInTheDocument()
    expect(screen.getByText(/Discount \(.*10%.*\)/)).toBeInTheDocument()
    expect(screen.getByText('Payment Details')).toBeInTheDocument()
    expect(screen.getByText('Paid')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'ORD-000041' })).toHaveAttribute('href', '/orders/41')
    expect(screen.getByRole('button', { name: 'Void Receipt' })).toBeInTheDocument()
  })

  it('a void receipt shows why, and offers no second void', async () => {
    open('/receipts/1', ...ADMIN)
    api.routes['GET /api/v1/receipts/1'] = () => json({ ...detail, isVoid: true, voidReason: 'Wrong item', voidDate: '2026-10-31T11:00:00Z', voidedBy: 'Layla Admin' })

    expect(await screen.findByText('This receipt has been voided.')).toBeInTheDocument()
    expect(screen.getByText(/Wrong item/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Void Receipt' })).not.toBeInTheDocument()
  })

  it('a viewer cannot void from the details', async () => {
    open('/receipts/1', Permissions.Sales_View)

    await screen.findByRole('heading', { name: 'RCP-20261031-001' })

    expect(screen.queryByRole('button', { name: 'Void Receipt' })).not.toBeInTheDocument()
  })

  it('shows the not-found card for a receipt that does not exist', async () => {
    open('/receipts/99', ...ADMIN)
    api.routes['GET /api/v1/receipts/99'] = () => problem(404, 'receipt_not_found')

    expect(await screen.findByText('That receipt was not found.')).toBeInTheDocument()
  })
})
