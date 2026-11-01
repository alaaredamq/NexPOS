import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClientProvider } from '@tanstack/react-query'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { queryClient } from '@/api/queryClient'
import { toast } from '@/ui/toast'
import { UiHost } from '@/ui/UiHost'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { CustomerDebtDialog } from './CustomerDebtDialog'
import { orderHref } from './CustomerDetailsPage'
import routes from './routes'

const customer = (id: number, firstName: string, overrides = {}) => ({
  id,
  firstName,
  lastName: 'Haddad',
  email: `${firstName.toLowerCase()}@example.com`,
  phoneNumber: '70123456',
  address: 'Hamra street',
  isActive: true,
  balance: 0,
  ordersCount: 2,
  totalSpent: 30,
  createdAt: '2026-01-05T10:00:00Z',
  ...overrides,
})

const list = (items: ReturnType<typeof customer>[]) => ({ items, total: items.length, page: 1, pageSize: 25 })
const summary = { total: 2, active: 1, owing: 1, totalOwed: 45.5, totalSpent: 60, currencyNotation: '$' }

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'customers')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/customers'] = () => json(list([customer(1, 'Mona', { balance: 45.5 }), customer(2, 'Omar', { isActive: false })]))
  api.routes['GET /api/v1/customers/summary'] = () => json(summary)
  return mountModule(path, routes)
}

const ADMIN = [Permissions.Customers_View, Permissions.Customers_Create, Permissions.Customers_Edit, Permissions.Customers_Delete]

describe('customers list', () => {
  it('shows the rows, the balances and the four tiles', async () => {
    open('/customers', ...ADMIN)

    expect(await screen.findByRole('link', { name: 'Mona Haddad' })).toHaveAttribute('href', '/customers/1')
    expect(screen.getByRole('link', { name: 'Omar Haddad' })).toBeInTheDocument()
    expect(await screen.findByText('Outstanding Debt')).toBeInTheDocument()
    expect(await screen.findByText('1 customers owe you')).toBeInTheDocument()
    // The row's balance and the tile are both $ 45.50; a settled customer shows a plain zero.
    expect((await screen.findAllByText(/45\.50/)).length).toBeGreaterThanOrEqual(2)
    expect(screen.getAllByText(/\b0\.00/).length).toBeGreaterThan(0)
  })

  it('asks the API for the sort the user clicked, and keeps it in the URL', async () => {
    const router = open('/customers', ...ADMIN)
    await screen.findByRole('link', { name: 'Mona Haddad' })

    fireEvent.click(screen.getByRole('button', { name: /Balance/ }))

    await waitFor(() => expect(router.state.location.search).toBe('?sort=balance&dir=asc'))
    await waitFor(() => expect(api.calls.some((c) => c.url.includes('sort=balance') && c.url.includes('dir=asc'))).toBe(true))
  })

  it('a cashier (view + add only) can add a customer but has no edit, debt or delete buttons', async () => {
    open('/customers', Permissions.Customers_View, Permissions.Customers_Create)

    await screen.findByRole('link', { name: 'Mona Haddad' })

    expect(screen.getAllByRole('button', { name: 'Add Customer' }).length).toBeGreaterThan(0)
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add Debt Transaction' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
  })

  it('without Customers.View the route bounces to the forbidden page', async () => {
    const router = open('/customers', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('create and edit', () => {
  it('refuses a one-letter name and a bad email without calling the API', async () => {
    open('/customers', ...ADMIN)
    await screen.findByRole('link', { name: 'Mona Haddad' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Add Customer' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add Customer' })
    fireEvent.change(within(dialog).getByLabelText(/First Name/), { target: { value: 'x' } })
    fireEvent.change(within(dialog).getByLabelText(/Last Name/), { target: { value: 'Okay' } })
    fireEvent.change(within(dialog).getByLabelText(/Email Address/), { target: { value: 'not-an-email' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Enter 2 to 100 characters.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a valid email address.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('posts trimmed text with empty optional fields as null, then refreshes the list', async () => {
    open('/customers', ...ADMIN)
    api.routes['POST /api/v1/customers'] = () => json(customer(9, 'Lina'), 201)
    await screen.findByRole('link', { name: 'Mona Haddad' })
    const listCalls = () => api.calls.filter((c) => c.method === 'GET' && c.url.startsWith('/api/v1/customers?')).length
    const before = listCalls()

    fireEvent.click(screen.getAllByRole('button', { name: 'Add Customer' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Add Customer' })
    fireEvent.change(within(dialog).getByLabelText(/First Name/), { target: { value: '  Lina ' } })
    fireEvent.change(within(dialog).getByLabelText(/Last Name/), { target: { value: 'Saad  ' } })
    fireEvent.change(within(dialog).getByLabelText(/Phone Number/), { target: { value: ' 71000000 ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ firstName: 'Lina', lastName: 'Saad', email: null, phoneNumber: '71000000', address: null, isActive: true }),
    )
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    await waitFor(() => expect(listCalls()).toBeGreaterThan(before))
    expect(await screen.findByText('Customer created successfully!')).toBeInTheDocument()
  })

  it('opens the edit dialog filled in and shows a server field error without closing', async () => {
    open('/customers', ...ADMIN)
    api.routes['PUT /api/v1/customers/1'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { email: ['Email already used'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await screen.findByRole('link', { name: 'Mona Haddad' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Edit Customer' })
    expect(within(dialog).getByLabelText(/First Name/)).toHaveValue('Mona')
    expect(within(dialog).getByLabelText(/Email Address/)).toHaveValue('mona@example.com')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Email already used')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Edit Customer' })).toBeInTheDocument()
  })
})

describe('delete', () => {
  it('confirms, deletes, and says so', async () => {
    open('/customers', ...ADMIN)
    api.routes['DELETE /api/v1/customers/2'] = () => new Response(null, { status: 204 })
    await screen.findByRole('link', { name: 'Omar Haddad' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[1]!)
    const dialog = await screen.findByRole('dialog', { name: 'Confirm Delete' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'DELETE' && c.url === '/api/v1/customers/2')).toBe(true))
    expect(await screen.findByText('Customer deleted successfully!')).toBeInTheDocument()
  })

  it("shows the server's sentence in the dialog when the customer still has a balance, and deletes nothing", async () => {
    open('/customers', ...ADMIN)
    api.routes['DELETE /api/v1/customers/1'] = () => problem(409, 'customer_has_balance', 'This customer owes you 45.50. Settle the balance before deleting.')
    await screen.findByRole('link', { name: 'Mona Haddad' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Confirm Delete' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await within(dialog).findByText(/owes you 45\.50/)).toBeInTheDocument()
    expect(screen.queryByText('Customer deleted successfully!')).not.toBeInTheDocument()
    // The confirm dialog lives in a global store: close it so it does not leak into the next test.
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })
})

describe('debt dialog from the list', () => {
  const openDialog = async () => {
    open('/customers', ...ADMIN)
    await screen.findByRole('link', { name: 'Mona Haddad' })
    fireEvent.click(screen.getAllByRole('button', { name: 'Add Debt Transaction' })[0]!)
    return await screen.findByRole('dialog', { name: 'Add Debt Transaction' })
  }

  it('refuses an empty amount without calling the API', async () => {
    const dialog = await openDialog()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Enter an amount greater than zero.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('records "customer owes me" by default, with the typed note trimmed', async () => {
    const dialog = await openDialog()
    api.routes['POST /api/v1/customers/1/debt-transactions'] = () =>
      json({ transaction: { id: 5, createdAt: '2026-02-01T10:00:00Z', type: 'ManualAdjustment', amount: 20 }, balance: 65.5 }, 201)

    fireEvent.change(within(dialog).getByLabelText(/Amount/), { target: { value: '20' } })
    fireEvent.change(within(dialog).getByLabelText(/Note/), { target: { value: '  credit sale ' } })
    expect(within(dialog).getByText(/Balance after:.*65\.50/)).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ amount: 20, customerOwesMore: true, note: 'credit sale' }))
    expect(await screen.findByText('Debt updated successfully!')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('sends a payment as "owes less" and warns, before saving, when it is more than the customer owes', async () => {
    const dialog = await openDialog()
    api.routes['POST /api/v1/customers/1/debt-transactions'] = () =>
      json({ transaction: { id: 6, createdAt: '2026-02-01T10:00:00Z', type: 'ManualAdjustment', amount: -50 }, balance: -4.5 }, 201)

    fireEvent.click(within(dialog).getByRole('radio', { name: /I got paid/ }))
    fireEvent.change(within(dialog).getByLabelText(/Amount/), { target: { value: '50' } })

    expect(within(dialog).getByText(/the balance becomes -.*4\.50.*so you will owe the customer/)).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ amount: 50, customerOwesMore: false, note: null }))
  })

  it('does not warn about an overpayment for a payment inside the balance', async () => {
    const dialog = await openDialog()

    fireEvent.click(within(dialog).getByRole('radio', { name: /I got paid/ }))
    fireEvent.change(within(dialog).getByLabelText(/Amount/), { target: { value: '10' } })

    expect(within(dialog).getByText(/Balance after:.*35\.50/)).toBeInTheDocument()
    expect(within(dialog).queryByText(/so you will owe the customer/)).not.toBeInTheDocument()
  })
})

describe('CustomerDebtDialog as the POS reuses it', () => {
  it('works on its own: reads the currency from the API when none is passed, reports the new balance, and closes', async () => {
    api = fakeApi(sessionWith(Permissions.Customers_Edit))
    api.routes['GET /api/v1/customers/summary'] = () => json(summary)
    api.routes['POST /api/v1/customers/7/debt-transactions'] = () =>
      json({ transaction: { id: 1, createdAt: '2026-02-01T10:00:00Z', type: 'ManualAdjustment', amount: 12 }, balance: 12 }, 201)
    const onSaved = vi.fn()
    const onClose = vi.fn()
    queryClient.clear()
    render(
      <QueryClientProvider client={queryClient}>
        <CustomerDebtDialog customer={{ id: 7, name: 'Walk In' }} onClose={onClose} onSaved={onSaved} />
        <UiHost />
      </QueryClientProvider>,
    )

    const dialog = await screen.findByRole('dialog', { name: 'Add Debt Transaction' })
    expect(within(dialog).getByText('Walk In')).toBeInTheDocument()
    fireEvent.change(within(dialog).getByLabelText(/Amount/), { target: { value: '12' } })
    await waitFor(() => expect(api.calls.some((c) => c.url === '/api/v1/customers/summary')).toBe(true))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ balance: 12 })))
    expect(onClose).toHaveBeenCalled()
  })
})

describe('details', () => {
  const details = (balance: number, overrides = {}) => ({
    customer: customer(1, 'Mona', { balance, ordersCount: 2, totalSpent: 30 }),
    city: 'Beirut',
    country: 'Lebanon',
    postalCode: '1100',
    currencyNotation: '$',
    averageOrder: 15,
    lastOrderAt: '2026-03-02T09:00:00Z',
    allOrdersCount: 12,
    recentOrders: [
      { id: 41, createdAt: '2026-03-02T09:00:00Z', totalAmount: 20, currencyNotation: '$', status: 'Delivered' },
      { id: 40, createdAt: '2026-03-01T09:00:00Z', totalAmount: 10, currencyNotation: '$', status: 'Cancelled' },
    ],
    ...overrides,
  })
  const ledger = {
    items: [
      { id: 3, createdAt: '2026-03-02T09:00:00Z', type: 'Payment', amount: -10, note: 'Cash', recordedBy: 'layla' },
      { id: 2, createdAt: '2026-03-01T09:00:00Z', type: 'Sale', amount: 55.5, orderId: 41, recordedBy: 'sara' },
    ],
    total: 2,
    page: 1,
    pageSize: 10,
  }

  const openDetails = (balance: number, ...permissions: string[]) => {
    open('/customers/1', ...permissions)
    api.routes['GET /api/v1/customers/1'] = () => json(details(balance))
    api.routes['GET /api/v1/customers/1/debt-transactions'] = () => json(ledger)
  }

  it('shows who owes whom, the ledger with signs, and the latest orders with their status', async () => {
    openDetails(45.5, ...ADMIN, Permissions.Orders_View)

    expect(await screen.findByRole('heading', { name: 'Mona Haddad' })).toBeInTheDocument()
    expect(await screen.findByText(/Customer owes me:.*45\.50/)).toBeInTheDocument()
    expect(screen.getByText('Receivable')).toBeInTheDocument()
    expect(await screen.findByText(/^\+.*55\.50/)).toBeInTheDocument()
    expect(screen.getByText(/−.*10\.00/)).toBeInTheDocument()
    expect(screen.getByText('Order #41')).toBeInTheDocument() // a sale entry without a note names its order
    expect(screen.getByText('Cash')).toBeInTheDocument()
    expect(screen.getByText('Delivered')).toBeInTheDocument()
    expect(screen.getByText('Cancelled')).toBeInTheDocument()
    expect(screen.getByText('Hamra street, Beirut, Lebanon - 1100')).toBeInTheDocument()
  })

  it('says "I owe the customer" for a negative balance and "balanced" for zero', async () => {
    openDetails(-30, ...ADMIN)
    expect(await screen.findByText(/I owe customer:.*30\.00/)).toBeInTheDocument()
    expect(screen.getByText('Payable')).toBeInTheDocument()
  })

  it('links the orders through the nav: the SPA page once Orders is migrated, with the customer filter for "view all"', async () => {
    openDetails(0, ...ADMIN, Permissions.Orders_View)

    const orderLink = await screen.findByRole('link', { name: '#41' })
    expect(orderLink).toHaveAttribute('href', '/app/orders/41')
    expect(screen.getAllByRole('link', { name: /View All Orders \(12\)/ })[0]).toHaveAttribute('href', '/app/orders?customerId=1')
  })

  it('hides the order links without Orders.View', async () => {
    openDetails(0, ...ADMIN)

    await screen.findByText('#41')
    expect(screen.queryByRole('link', { name: '#41' })).not.toBeInTheDocument()
  })

  it('hides the edit, debt and delete controls from a user who may only view', async () => {
    openDetails(10, Permissions.Customers_View)

    await screen.findByRole('heading', { name: 'Mona Haddad' })

    expect(screen.queryByRole('button', { name: 'Add Transaction' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit Customer' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete Customer' })).not.toBeInTheDocument()
  })

  it('opens the debt dialog from the details page with the balance known, and refetches the page after saving', async () => {
    openDetails(45.5, ...ADMIN)
    api.routes['POST /api/v1/customers/1/debt-transactions'] = () =>
      json({ transaction: { id: 9, createdAt: '2026-03-03T09:00:00Z', type: 'ManualAdjustment', amount: 5 }, balance: 50.5 }, 201)
    await screen.findByRole('heading', { name: 'Mona Haddad' })
    const detailCalls = () => api.calls.filter((c) => c.url === '/api/v1/customers/1').length
    const before = detailCalls()

    fireEvent.click(screen.getByRole('button', { name: 'Add Transaction' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Debt Transaction' })
    fireEvent.change(within(dialog).getByLabelText(/Amount/), { target: { value: '5' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(detailCalls()).toBeGreaterThan(before))
  })

  it('deletes from the details page and returns to the list', async () => {
    openDetails(0, ...ADMIN)
    api.routes['DELETE /api/v1/customers/1'] = () => new Response(null, { status: 204 })
    await screen.findByRole('heading', { name: 'Mona Haddad' })

    fireEvent.click(screen.getByRole('button', { name: 'Delete Customer' }))
    const dialog = await screen.findByRole('dialog', { name: 'Are you sure?' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes, delete it!' }))

    expect(await screen.findByText('Customer deleted successfully!')).toBeInTheDocument()
  })

  it('shows the not-found page for a customer that does not exist', async () => {
    open('/customers/99', ...ADMIN)
    api.routes['GET /api/v1/customers/99'] = () => problem(404, 'not_found')
    api.routes['GET /api/v1/customers/99/debt-transactions'] = () => problem(404, 'not_found')

    expect(await screen.findByText(/not found|doesn't exist|no longer/i)).toBeInTheDocument()
  })
})

describe('orderHref', () => {
  it('uses the SPA path once Orders is migrated and the Razor details URL before', () => {
    expect(orderHref('/app/orders', 7)).toBe('/app/orders/7')
    expect(orderHref('/en/Orders', 7)).toBe('/en/Orders/Details/7')
    expect(orderHref('/ar/Orders', 7)).toBe('/ar/Orders/Details/7')
  })
})
