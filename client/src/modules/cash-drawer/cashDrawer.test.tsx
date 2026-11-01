import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const drop = (id: number, amount: number, reason = 'Safe Deposit') => ({ id, amount, droppedAt: '2026-11-01T10:30:00Z', reason, notes: null })

const session = (overrides = {}) => ({
  id: 7,
  userId: 2,
  cashierName: 'Sara Cashier',
  isOpen: true,
  openedAt: '2026-11-01T08:00:00Z',
  closedAt: null,
  openingCash: 100,
  expectedCash: 76.4,
  actualCash: null,
  cashDifference: null,
  totalSales: 6.4,
  totalTransactions: 1,
  totalDiscounts: 0,
  totalAdjustments: 0,
  totalCashDrops: 30,
  openingNotes: null,
  closingNotes: null,
  cashDrops: [drop(1, 30)],
  ...overrides,
})

const OPEN = { status: { isOpen: true, sessionId: 7, openingCash: 100 }, session: session() }
const CLOSED = { status: { isOpen: false, openingCash: 0 } }

const MANAGER = [Permissions.Sales_View, Permissions.Sales_Edit]

let api: FakeApi

/** Text of an element without the direction-isolate marks around money and numbers. */
const text = (element: Element | null | undefined) => (element?.textContent ?? '').replace(/[⁦-⁩]/g, '')
const main = () => within(screen.getByRole('main'))

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'cashDrawer')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path: string, current: unknown, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/cash-drawer/current'] = () => json(current)
  api.routes['GET /api/v1/cash-drawer/sessions/7'] = () => json((current as typeof OPEN).session)
  return mountModule(path, routes)
}

describe('the drawer page', () => {
  it('shows the running figures of the open session and its cash drops', async () => {
    open('/cash-drawer', OPEN, ...MANAGER)

    expect(await screen.findByText('Current Open Session')).toBeInTheDocument()
    expect(main().getByText('#7')).toBeInTheDocument()
    expect(main().getByText('Sara Cashier')).toBeInTheDocument()
    expect(text(main().getByText('Expected Cash').nextSibling as Element)).toBe('$ 76.40')
    expect(text(main().getByText('Total Sales').nextSibling as Element)).toBe('$ 6.40')
    expect(screen.getByRole('table', { name: 'Cash Drops' })).toHaveTextContent('Safe Deposit')
    expect(screen.getByRole('button', { name: 'Close Drawer' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cash Drop' })).toBeInTheDocument()
  })

  it('a closed drawer offers to open one', async () => {
    const router = open('/cash-drawer', CLOSED, ...MANAGER)

    expect(await screen.findByText('No Open Cash Drawer Session')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Close Drawer' })).not.toBeInTheDocument()

    fireEvent.click(screen.getAllByRole('button', { name: 'Open Cash Drawer' })[0]!)
    await waitFor(() => expect(router.state.location.pathname).toBe('/cash-drawer/open'))
  })

  it('Sales.View alone may look but not close or drop cash', async () => {
    open('/cash-drawer', OPEN, Permissions.Sales_View)

    await screen.findByText('Current Open Session')

    expect(screen.queryByRole('button', { name: 'Close Drawer' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cash Drop' })).not.toBeInTheDocument()
  })

  it('without Sales.View the route bounces to the forbidden page', async () => {
    const router = open('/cash-drawer', OPEN, Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('the close route needs Sales.Edit as well', async () => {
    const router = open('/cash-drawer/close/7', OPEN, Permissions.Sales_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('opening the drawer', () => {
  it('refuses an empty amount without calling the API, then opens with the float and notes', async () => {
    const router = open('/cash-drawer/open', CLOSED, ...MANAGER)
    api.routes['POST /api/v1/cash-drawer/open'] = () => json(session({ notes: null }), 201)
    const field = await screen.findByLabelText(/Starting Cash Amount/)

    fireEvent.click(screen.getAllByRole('button', { name: 'Open Cash Drawer' })[0]!)
    expect(await screen.findByText('Please enter a valid opening cash amount')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)

    fireEvent.change(field, { target: { value: '100' } })
    fireEvent.change(screen.getByLabelText(/Notes/), { target: { value: 'morning float' } })
    fireEvent.click(screen.getAllByRole('button', { name: 'Open Cash Drawer' })[0]!)

    await waitFor(() => expect(router.state.location.pathname).toBe('/cash-drawer'))
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ openingCash: 100, notes: 'morning float' })
  })

  it('shows the server refusal when a drawer is already open', async () => {
    open('/cash-drawer/open', CLOSED, ...MANAGER)
    api.routes['POST /api/v1/cash-drawer/open'] = () => problem(409, 'drawer_already_open', 'You already have an open cash drawer session.')
    fireEvent.change(await screen.findByLabelText(/Starting Cash Amount/), { target: { value: '5' } })

    fireEvent.click(screen.getAllByRole('button', { name: 'Open Cash Drawer' })[0]!)

    expect(await main().findByRole('alert')).toHaveTextContent('You already have an open cash drawer session.')
  })

  it('goes back to where the till sent the cashier from', async () => {
    const router = open('/cash-drawer/open?returnTo=%2Fpos', CLOSED, ...MANAGER)
    api.routes['POST /api/v1/cash-drawer/open'] = () => json(session(), 201)
    fireEvent.change(await screen.findByLabelText(/Starting Cash Amount/), { target: { value: '0' } })

    fireEvent.click(screen.getAllByRole('button', { name: 'Open Cash Drawer' })[0]!)

    await waitFor(() => expect(router.state.location.pathname).toBe('/pos'))
  })
})

describe('closing the drawer', () => {
  it('previews a shortage while typing and closes against the count', async () => {
    const router = open('/cash-drawer/close/7', OPEN, ...MANAGER)
    api.routes['POST /api/v1/cash-drawer/sessions/7/close'] = () => json(session({ isOpen: false, actualCash: 76.4, cashDifference: 0 }))
    const field = await screen.findByLabelText(/Actual Cash in Drawer/)

    fireEvent.change(field, { target: { value: '70' } })
    const preview = await screen.findByRole('status')
    expect(preview).toHaveTextContent('Cash Shortage')
    expect(text(preview)).toContain('$ 6.40')

    fireEvent.change(field, { target: { value: '76.40' } })
    expect(await screen.findByText('Perfect Balance')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText(/Closing Notes/), { target: { value: 'ok' } })
    fireEvent.click(screen.getByRole('button', { name: 'Close Cash Drawer' }))

    await waitFor(() => expect(router.state.location.pathname).toBe('/cash-drawer/z-report/7'))
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ actualCash: 76.4, notes: 'ok' })
  })

  it('a big difference without notes asks first, and cancelling closes nothing', async () => {
    open('/cash-drawer/close/7', OPEN, ...MANAGER)
    api.routes['POST /api/v1/cash-drawer/sessions/7/close'] = () => json(session({ isOpen: false }))
    fireEvent.change(await screen.findByLabelText(/Actual Cash in Drawer/), { target: { value: '50' } })

    fireEvent.click(screen.getByRole('button', { name: 'Close Cash Drawer' }))
    const dialog = await screen.findByRole('dialog', { name: 'Close Cash Drawer' })
    expect(dialog).toHaveTextContent('26.40')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('counting zero is allowed (everything was dropped)', async () => {
    open('/cash-drawer/close/7', OPEN, ...MANAGER)
    api.routes['POST /api/v1/cash-drawer/sessions/7/close'] = () => json(session({ isOpen: false }))
    fireEvent.change(await screen.findByLabelText(/Actual Cash in Drawer/), { target: { value: '0' } })
    fireEvent.change(screen.getByLabelText(/Closing Notes/), { target: { value: 'all dropped' } })

    fireEvent.click(screen.getByRole('button', { name: 'Close Cash Drawer' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ actualCash: 0, notes: 'all dropped' }))
  })

  it('an already closed session points to its Z report instead of the form', async () => {
    open('/cash-drawer/close/7', { status: CLOSED.status, session: session({ isOpen: false }) }, ...MANAGER)

    expect(await screen.findByText('No Open Cash Drawer Session')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Z-Report' })).toBeInTheDocument()
    expect(screen.queryByLabelText(/Actual Cash in Drawer/)).not.toBeInTheDocument()
  })
})

describe('cash drop dialog', () => {
  it('needs a positive amount, then posts it with the English reason the old page stored', async () => {
    open('/cash-drawer', OPEN, ...MANAGER)
    api.routes['POST /api/v1/cash-drawer/sessions/7/drops'] = () => json(drop(2, 25), 201)
    fireEvent.click(await screen.findByRole('button', { name: 'Cash Drop' }))
    const dialog = await screen.findByRole('dialog', { name: 'Record Cash Drop' })

    fireEvent.click(within(dialog).getByRole('button', { name: 'Record Cash Drop' }))
    expect(await within(dialog).findByText('Please enter a valid amount')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)

    fireEvent.change(within(dialog).getByLabelText(/Amount/), { target: { value: '25' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Record Cash Drop' }))

    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Record Cash Drop' })).not.toBeInTheDocument())
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ amount: 25, reason: 'Safe Deposit', notes: null })
  })
})

describe('history', () => {
  const row = (id: number, overrides = {}) => ({
    id,
    isOpen: false,
    openedAt: '2026-10-30T08:00:00Z',
    closedAt: '2026-10-30T17:00:00Z',
    openingCash: 100,
    closingCash: 120,
    totalSales: 45,
    totalTransactions: 6,
    cashDifference: -2.5,
    ...overrides,
  })

  it('lists sessions and sends the local-day range to the API', async () => {
    open('/cash-drawer/history', OPEN, ...MANAGER)
    api.routes['GET /api/v1/cash-drawer/sessions'] = () => json({ items: [row(5), row(6, { isOpen: true, closedAt: null, closingCash: null, totalSales: null, totalTransactions: null, cashDifference: null })], total: 2, page: 1, pageSize: 25 })

    expect(await screen.findByText('#5')).toBeInTheDocument()
    expect(screen.getByText('#6')).toBeInTheDocument()
    expect(text(screen.getByRole('table'))).toContain('−$ 2.50') // a shortage
    expect(screen.getByRole('button', { name: 'Z-Report' })).toBeInTheDocument() // closed session
    expect(screen.getAllByRole('button', { name: 'Close Drawer' }).length).toBeGreaterThan(0) // the open one
    const call = api.calls.find((c) => c.url.startsWith('/api/v1/cash-drawer/sessions?'))!
    expect(call.url).toMatch(/from=\d{4}-\d{2}-\d{2}/)
    expect(call.url).toMatch(/to=\d{4}-\d{2}-\d{2}/)
  })
})

describe('Z report', () => {
  const report = {
    sessionId: 7,
    cashierName: 'Sara Cashier',
    openedAt: '2026-11-01T08:00:00Z',
    closedAt: '2026-11-01T17:00:00Z',
    openingCash: 100,
    totalTransactions: 2,
    totalSales: 12.9,
    totalDiscounts: 0,
    totalAdjustments: 0,
    netSales: 12.9,
    cashSales: 12.9,
    cardSales: 0,
    otherPayments: 0,
    totalCashDrops: 35,
    cashDropCount: 2,
    expectedCash: 77.9,
    actualCash: 70,
    cashDifference: -7.9,
    cashDrops: [drop(1, 25, 'Bank Deposit'), drop(2, 10, 'Other')],
    orders: [{ orderId: 11, orderNumber: 'ORD-000011', orderDate: '2026-11-01T09:00:00Z', customerName: '', totalAmount: 6.4, paymentMethod: 'Cash' }],
  }

  it('repeats the numbers, words a walk-in itself and shows the shortage', async () => {
    open('/cash-drawer/z-report/7', CLOSED, ...MANAGER)
    api.routes['GET /api/v1/cash-drawer/sessions/7/z-report'] = () => json(report)

    expect(await screen.findByLabelText('Z-Report', { selector: 'article' })).toBeInTheDocument()
    expect(text(screen.getByText('Expected Cash').nextSibling as Element)).toBe('$ 77.90')
    expect(text(screen.getByText('Actual Cash').nextSibling as Element)).toBe('$ 70.00')
    expect(text(screen.getByText('Net Sales').nextSibling as Element)).toBe('$ 12.90')
    expect(screen.getByText('SHORTAGE')).toBeInTheDocument()
    expect(text(screen.getByText('Cash Drops (2)').nextSibling as Element)).toBe('−$ 35.00')
    expect(screen.getByText('Walk-in Customer')).toBeInTheDocument()
    expect(screen.getByText('Bank Deposit')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Print on receipt paper' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Download PDF' })).toBeInTheDocument()
  })

  it('sends the report to the receipt printer and says what the server said', async () => {
    open('/cash-drawer/z-report/7', CLOSED, ...MANAGER)
    api.routes['GET /api/v1/cash-drawer/sessions/7/z-report'] = () => json(report)
    api.routes['POST /api/v1/cash-drawer/sessions/7/z-report/print'] = () => json({ printed: false, message: 'Could not print.', pdfUrl: '/api/v1/cash-drawer/sessions/7/z-report.pdf' })

    fireEvent.click(await screen.findByRole('button', { name: 'Print on receipt paper' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url.endsWith('/z-report/print'))).toBe(true))
    expect(await screen.findByText('Could not print.')).toBeInTheDocument()
  })

  it('an open session has no report: the page says why and offers the close page', async () => {
    const router = open('/cash-drawer/z-report/7', OPEN, ...MANAGER)
    api.routes['GET /api/v1/cash-drawer/sessions/7/z-report'] = () => problem(409, 'session_still_open', 'The Z-Report is only available once the session is closed.')

    expect(await screen.findByText('The Z-Report is only available once the session is closed.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Close Cash Drawer' }))
    await waitFor(() => expect(router.state.location.pathname).toBe('/cash-drawer/close/7'))
  })
})
