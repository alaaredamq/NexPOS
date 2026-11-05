import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const summary = {
  currencyNotation: '$',
  today: { sales: 80, profit: 20, orders: 4 },
  thisMonth: { sales: 900, profit: 250, orders: 30 },
  thisYear: { sales: 9000, profit: 2345, orders: 300 },
  averageOrderValue: 30,
}

const report = {
  from: '2026-10-03',
  to: '2026-10-05',
  currencyNotation: '$',
  totalSales: 150,
  totalProfit: 61.5,
  totalOrders: 5,
  averageOrderValue: 30,
  totalItemsSold: 12.5,
  daily: [
    { date: '2026-10-03', sales: 50, orders: 2 },
    { date: '2026-10-04', sales: 0, orders: 0 },
    { date: '2026-10-05', sales: 100, orders: 3 },
  ],
  topProducts: [{ productId: 1, name: 'Cola', quantitySold: 8, revenue: 96, averagePrice: 12 }],
  categories: [{ categoryId: 1, name: 'Drinks', sales: 150, percentage: 100 }],
  paymentMethods: [
    { paymentMethodId: 1, name: 'Cash', sales: 100, receipts: 3, percentage: 66.7 },
    { paymentMethodId: 2, name: 'Card', sales: 50, receipts: 2, percentage: 33.3 },
  ],
}

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'reports')
})

beforeEach(async () => {
  localStorage.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path = '/reports?from=2026-10-03&to=2026-10-05', ...permissions: string[]) {
  api = fakeApi(sessionWith(...(permissions.length ? permissions : [Permissions.Reports_View])))
  api.routes['GET /api/v1/reports/summary'] = () => json(summary)
  api.routes['GET /api/v1/reports/sales'] = () => json(report)
  return mountModule(path, routes)
}

describe('reports', () => {
  it('shows the period tiles and the totals of the range the server computed', async () => {
    open()

    expect(await screen.findByText(/9,000\.00/)).toBeInTheDocument()
    expect(screen.getByText('30 orders', { exact: false })).toBeInTheDocument()
    expect(await screen.findByText('Total Sales')).toBeInTheDocument()
    expect(screen.getAllByText(/150\.00/).length).toBeGreaterThan(0)
    expect(screen.getByText('12.5')).toBeInTheDocument()
  })

  it("shows this year's profit and the profit of the range", async () => {
    open()

    expect(await screen.findByText(/2,345\.00/)).toBeInTheDocument()
    expect(screen.getByText("This Year's Profit")).toBeInTheDocument()
    expect(await screen.findByText(/61\.50/)).toBeInTheDocument()
    expect(screen.getByText('Total Profit')).toBeInTheDocument()
  })

  it('sets the range to 1 January through today with the This year chip', async () => {
    open()
    await screen.findByText('Total Sales')

    fireEvent.click(screen.getByRole('button', { name: 'This year' }))

    await waitFor(() => {
      const call = api.calls.filter((c) => c.url.includes('/reports/sales')).at(-1)!
      const params = new URL(call.url, 'http://x').searchParams
      expect(params.get('from')).toBe(`${new Date().getFullYear()}-01-01`)
    })
  })

  it('asks the API for exactly the days in the URL', async () => {
    open()
    await screen.findByText('Total Sales')

    const call = api.calls.find((c) => c.url.includes('/reports/sales'))
    expect(call?.url).toContain('from=2026-10-03')
    expect(call?.url).toContain('to=2026-10-05')
  })

  it('ranks the products, splits the categories and lists the payment methods', async () => {
    open()

    const products = await screen.findByRole('table', { name: 'Top Products' })
    expect(within(products).getByText('Cola')).toBeInTheDocument()
    expect(within(products).getByText(/96\.00/)).toBeInTheDocument()
    expect(screen.getByText('Drinks')).toBeInTheDocument()
    const methods = screen.getByRole('list', { name: 'Sales by Payment Method' })
    expect(within(methods).getByText('Cash')).toBeInTheDocument()
    expect(within(methods).getByText('Card')).toBeInTheDocument()
  })

  it('falls back to the last 30 days when the URL range is missing or backwards', async () => {
    open('/reports?from=2026-10-09&to=2026-10-01')
    await screen.findByText('Total Sales')

    const call = api.calls.find((c) => c.url.includes('/reports/sales'))!
    const params = new URL(call.url, 'http://x').searchParams
    const days = (Date.parse(params.get('to')!) - Date.parse(params.get('from')!)) / 86_400_000
    expect(days).toBe(29)
  })

  it('downloads the CSV for the range on screen', async () => {
    open()
    await screen.findByText('Total Sales')
    api.routes['GET /api/v1/reports/sales.csv'] = () => new Response('a,b', { headers: { 'Content-Type': 'text/csv' } })
    URL.createObjectURL = vi.fn(() => 'blob:x')

    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))

    await waitFor(() => expect(api.calls.some((c) => c.url.includes('/reports/sales.csv') && c.url.includes('from=2026-10-03'))).toBe(true))
  })

  it('shows the server message when the range is refused', async () => {
    open()
    api.routes['GET /api/v1/reports/sales'] = () => problem(400, 'validation_failed', 'Choose a range of at most 3660 days.')
    fireEvent.click(await screen.findByRole('button', { name: 'This year' }))

    expect(await screen.findByRole('alert')).toBeInTheDocument()
  })

  it('says so, instead of drawing empty charts, when nothing was sold', async () => {
    open()
    api.routes['GET /api/v1/reports/sales'] = () => json({ ...report, totalSales: 0, totalOrders: 0, totalItemsSold: 0, averageOrderValue: 0, topProducts: [], categories: [], paymentMethods: [] })
    fireEvent.click(await screen.findByRole('button', { name: 'This year' }))

    expect((await screen.findAllByText('No sales in this range.')).length).toBeGreaterThan(0)
    expect(screen.queryByRole('table', { name: 'Top Products' })).not.toBeInTheDocument()
  })
})
