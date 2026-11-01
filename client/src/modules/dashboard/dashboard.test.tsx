import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { navItem, section } from '@/test/sessionFixture'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const today = new Date()
const day = (offset: number) => {
  const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() + offset)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

const overview = {
  currencyNotation: '$',
  monthSales: 1250.5,
  monthSalesChangePercent: 12.5,
  monthOrders: 42,
  monthOrdersChange: -3,
  todaySales: 80,
  todayOrders: 4,
  monthProfit: 300,
  monthProfitChangePercent: 0,
  customerCount: 17,
  productCount: 230,
  recentOrders: [
    { id: 9, customerName: null, amount: 12.5, currencyNotation: '$', orderDate: '2026-11-01T10:00:00Z', status: 4, paymentMethod: 'Cash' },
    { id: 8, customerName: 'Layla Haddad', amount: 30, currencyNotation: '$', orderDate: '2026-11-01T09:00:00Z', status: 3, paymentMethod: null },
  ],
  categories: [
    { name: 'Drinks', sales: 750, percentage: 60 },
    { name: 'Snacks', sales: 500.5, percentage: 40 },
  ],
}

const trend = (period: string) => ({
  period,
  currencyNotation: '$',
  points: [{ date: day(0), sales: 80, profit: 25 }],
})

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'dashboard', 'orders')
})

beforeEach(async () => {
  localStorage.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(...permissions: string[]) {
  const session = sessionWith(...permissions)
  session.nav = [
    section('Sales', [
      navItem('Dashboard', { migrated: true, href: '/app/dashboard' }),
      navItem('POS'),
      navItem('Orders', { migrated: true, href: '/app/orders' }),
      navItem('Customers'),
    ]),
  ]
  api = fakeApi(session)
  api.routes['GET /api/v1/dashboard'] = () => json(overview)
  api.routes['GET /api/v1/dashboard/sales-trend'] = (_init, url) => json(trend(new URL(url, 'http://x').searchParams.get('period') ?? 'month'))
  return mountModule('/dashboard', routes)
}

describe('dashboard', () => {
  it('shows what the server computed: month tiles, change vs last month, today and the counts', async () => {
    open(Permissions.Dashboard_View, Permissions.Orders_View)

    expect(await screen.findByText(/1,250\.50/)).toBeInTheDocument()
    expect(screen.getByText('Revenue this month')).toBeInTheDocument()
    expect(screen.getByText(/\+12\.5%/)).toBeInTheDocument()
    expect(screen.getByText(/−3\W+orders vs last month/)).toBeInTheDocument()
    expect(screen.getByText('4 orders today')).toBeInTheDocument()
    expect(screen.getByText('230')).toBeInTheDocument()
    expect(screen.getByText('17')).toBeInTheDocument()
  })

  it('lists the latest orders with their real status, a walk-in label and a link to the order', async () => {
    open(Permissions.Dashboard_View, Permissions.Orders_View)

    await screen.findByText('Walk-in')
    const table = screen.getByRole('table', { name: 'Recent Orders' })
    expect(within(table).getByText('Cancelled')).toBeInTheDocument()
    expect(within(table).getByText('Delivered')).toBeInTheDocument()
    expect(within(table).getByText('Walk-in')).toBeInTheDocument()
    expect(within(table).getByText('Layla Haddad')).toBeInTheDocument()
    expect(within(table).getByRole('link', { name: '#9' })).toHaveAttribute('href', '/orders/9')
  })

  it('draws the sales by category with each name and share in the legend', async () => {
    open(Permissions.Dashboard_View)

    const legend = (await screen.findByText('Drinks')).closest('li')!
    expect(legend).toHaveTextContent('60%')
    expect(screen.getByText('Snacks').closest('li')).toHaveTextContent('40%')
  })

  it('asks for another period when a chip is chosen and keeps the chart on screen meanwhile', async () => {
    open(Permissions.Dashboard_View)
    await screen.findByText(/1,250\.50/)

    fireEvent.click(screen.getByRole('button', { name: 'Year' }))

    await waitFor(() => expect(api.calls.some((c) => c.url.includes('sales-trend') && c.url.includes('period=year'))).toBe(true))
    expect(screen.getByRole('button', { name: 'Year' })).toHaveAttribute('aria-pressed', 'true')
  })

  it('offers the same numbers as a table', async () => {
    open(Permissions.Dashboard_View)
    await screen.findByText(/1,250\.50/)

    fireEvent.click(await screen.findByRole('button', { name: 'Table' }))

    const tables = await screen.findAllByRole('table')
    expect(tables.some((t) => within(t).queryByRole('columnheader', { name: 'Profit' }))).toBe(true)
  })

  it('shows only the shortcuts the role can open', async () => {
    open(Permissions.Dashboard_View)
    await screen.findByText(/1,250\.50/)

    const actions = screen.getByRole('region', { name: 'Quick Actions' })
    expect(within(actions).getByText('Point of Sale')).toBeInTheDocument()
    expect(within(actions).getByText('New Customer')).toBeInTheDocument()
    expect(within(actions).queryByText('View Reports')).not.toBeInTheDocument()
  })

  it('says so, with a retry, when the figures cannot be loaded', async () => {
    const session = sessionWith(Permissions.Dashboard_View)
    api = fakeApi(session)
    api.routes['GET /api/v1/dashboard'] = () => problem(403, 'forbidden', 'Not allowed')
    api.routes['GET /api/v1/dashboard/sales-trend'] = () => json(trend('month'))
    mountModule('/dashboard', routes)

    expect(await screen.findAllByRole('alert')).not.toHaveLength(0)
    expect(screen.getAllByRole('button', { name: /try again/i }).length).toBeGreaterThan(0)
  })
})
