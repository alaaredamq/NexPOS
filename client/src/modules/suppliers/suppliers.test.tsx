import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const supplier = (id: number, name: string, overrides = {}) => ({
  id,
  name,
  email: `${name.toLowerCase()}@example.com`,
  phone: '70123456',
  address: '1 Main St',
  city: 'Beirut',
  country: 'Lebanon',
  postalCode: null,
  discountRate: 5,
  isActive: true,
  currencyId: 1,
  currencyName: 'US Dollar',
  currencyNotation: '$',
  balance: 0,
  productCount: 4,
  ...overrides,
})

const page = <T,>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 25 })
const summary = { total: 2, active: 1, inactive: 1, withBalance: 1 }
const options = { currencies: [{ id: 1, name: 'US Dollar', notation: '$' }, { id: 2, name: 'Lebanese Pound', notation: 'L.L.' }], defaultCurrencyId: 1 }

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'suppliers', 'purchases')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

const ADMIN = [Permissions.Suppliers_View, Permissions.Suppliers_Create, Permissions.Suppliers_Edit, Permissions.Suppliers_Delete]

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/suppliers'] = () => json(page([supplier(1, 'Acme', { balance: 40 }), supplier(2, 'Basma', { isActive: false, balance: -15 })]))
  api.routes['GET /api/v1/suppliers/summary'] = () => json(summary)
  api.routes['GET /api/v1/suppliers/options'] = () => json(options)
  return mountModule(path, routes)
}

describe('suppliers list', () => {
  it('shows who is owed what, with the sign the server stored', async () => {
    open('/suppliers', ...ADMIN)

    // The first test of the file pays for the cold start of the whole app, which can pass the default 1 s on a busy machine.
    expect(await screen.findByRole('link', { name: 'Acme' }, { timeout: 5000 })).toHaveAttribute('href', '/suppliers/1')
    const table = screen.getByRole('table')
    const owed = within(table).getByText(/40\.00/)
    expect(owed).toHaveAttribute('data-sign', 'owe') // the shop owes this supplier
    expect(within(table).getByText(/15\.00/)).toHaveAttribute('data-sign', 'owed') // this supplier owes the shop
    expect(within(table).getByText('Inactive')).toBeInTheDocument()
    expect(await screen.findByText('With Balance')).toBeInTheDocument()
  })

  it('asks the API for the sort the user clicked, and keeps it in the URL', async () => {
    const router = open('/suppliers', ...ADMIN)
    await screen.findByRole('link', { name: 'Acme' })

    fireEvent.click(screen.getByRole('button', { name: /Balance/ }))

    await waitFor(() => expect(router.state.location.search).toBe('?sort=balance&dir=asc'))
    await waitFor(() => expect(api.calls.some((c) => c.url.includes('sort=balance') && c.url.includes('dir=asc'))).toBe(true))
  })

  it('a viewer sees no add button and no row actions', async () => {
    open('/suppliers', Permissions.Suppliers_View)
    await screen.findByRole('link', { name: 'Acme' })

    expect(screen.queryByRole('button', { name: 'Add Supplier' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add Transaction' })).not.toBeInTheDocument()
  })

  it('without Suppliers.View the route bounces to the forbidden page', async () => {
    const router = open('/suppliers', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('create and edit', () => {
  const openCreate = async () => {
    open('/suppliers', ...ADMIN)
    await screen.findByRole('link', { name: 'Acme' })
    fireEvent.click(screen.getAllByRole('button', { name: 'Add Supplier' })[0]!)
    return screen.findByRole('dialog', { name: 'Add Supplier' })
  }

  it('refuses a one-letter name without calling the API', async () => {
    const dialog = await openCreate()

    fireEvent.change(await within(dialog).findByLabelText(/Supplier Name/), { target: { value: 'x' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('The name must be between 2 and 200 characters.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('starts in the shop default currency, trims, and sends empty optional fields as null', async () => {
    const dialog = await openCreate()
    api.routes['POST /api/v1/suppliers'] = () => json(supplier(9, 'Delta'), 201)

    fireEvent.change(await within(dialog).findByLabelText(/Supplier Name/), { target: { value: '  Delta  ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() =>
      expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({
        name: 'Delta',
        email: null,
        phone: null,
        address: null,
        city: null,
        country: null,
        postalCode: null,
        currencyId: 1,
        discountRate: 0,
        isActive: true,
      }),
    )
    expect(await screen.findByText('Supplier created successfully!')).toBeInTheDocument()
  })

  it('rejects a malformed email in the dialog', async () => {
    const dialog = await openCreate()

    fireEvent.change(await within(dialog).findByLabelText(/Supplier Name/), { target: { value: 'Delta' } })
    fireEvent.change(within(dialog).getByLabelText('Email Address'), { target: { value: 'not-an-email' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('puts a server validation error on its field and keeps the dialog open', async () => {
    open('/suppliers', ...ADMIN)
    api.routes['PUT /api/v1/suppliers/1'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { phone: ['Phone is not valid'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await screen.findByRole('link', { name: 'Acme' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Edit' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Edit Supplier' })
    expect(await within(dialog).findByLabelText(/Supplier Name/)).toHaveValue('Acme')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Phone is not valid')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Edit Supplier' })).toBeInTheDocument()
  })
})

describe('debt dialog', () => {
  const openDebt = async () => {
    open('/suppliers', ...ADMIN)
    await screen.findByRole('link', { name: 'Acme' })
    fireEvent.click(screen.getAllByRole('button', { name: 'Add Transaction' })[0]!)
    return screen.findByRole('dialog', { name: 'Add Transaction' })
  }

  it('previews the balance and posts "I owe them more" as iOweMore: true', async () => {
    const dialog = await openDebt()
    api.routes['POST /api/v1/suppliers/1/debt-transactions'] = () => json({ entry: { id: 1, createdAt: '2026-01-01T00:00:00Z', kind: 'adjustment', amount: 25, note: 'Delivery', userName: 'Layla' }, balance: 65 }, 201)

    fireEvent.change(within(dialog).getByLabelText(/increases by/), { target: { value: '25' } })
    expect(within(dialog).getByRole('status')).toHaveTextContent(/65\.00/)
    fireEvent.change(within(dialog).getByLabelText('Note'), { target: { value: ' Delivery ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ amount: 25, iOweMore: true, note: 'Delivery' }))
    expect(await screen.findByText('Debt updated successfully!')).toBeInTheDocument()
  })

  it('"I paid them" lowers the balance, even below zero, and posts iOweMore: false', async () => {
    const dialog = await openDebt()
    api.routes['POST /api/v1/suppliers/1/debt-transactions'] = () => json({ entry: { id: 2, createdAt: '2026-01-01T00:00:00Z', kind: 'adjustment', amount: -100, note: null, userName: null }, balance: -60 }, 201)

    fireEvent.click(within(dialog).getByRole('button', { name: /I paid them/ }))
    fireEvent.change(within(dialog).getByLabelText(/decreases by/), { target: { value: '100' } })
    expect(within(dialog).getByRole('status')).toHaveTextContent(/60\.00/)
    expect(within(dialog).getByRole('status').textContent).toMatch(/[−-]/)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ amount: 100, iOweMore: false, note: null }))
  })

  it('refuses a zero amount without calling the API', async () => {
    const dialog = await openDebt()

    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    expect(await within(dialog).findByText('Enter an amount greater than zero.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })
})

describe('delete', () => {
  it('confirms, deletes, and says so', async () => {
    open('/suppliers', ...ADMIN)
    api.routes['DELETE /api/v1/suppliers/1'] = () => new Response(null, { status: 204 })
    await screen.findByRole('link', { name: 'Acme' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Are you sure?' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'DELETE' && c.url === '/api/v1/suppliers/1')).toBe(true))
    expect(await screen.findByText('Supplier deleted successfully!')).toBeInTheDocument()
  })

  it('shows the server explanation inside the dialog when money is still owed', async () => {
    open('/suppliers', ...ADMIN)
    api.routes['DELETE /api/v1/suppliers/1'] = () => problem(409, 'supplier_has_balance', 'There is an outstanding balance of $40.00 with this supplier.')
    await screen.findByRole('link', { name: 'Acme' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Delete' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Are you sure?' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await within(dialog).findByText(/outstanding balance of \$40\.00/)).toBeInTheDocument()
    expect(screen.queryByText('Supplier deleted successfully!')).not.toBeInTheDocument()
  })
})

describe('details', () => {
  const ledger = {
    items: [
      { id: 2, createdAt: '2026-02-01T10:00:00Z', kind: 'adjustment', amount: -30, note: 'Part payment', userName: 'Layla' },
      { id: 1, createdAt: '2026-01-01T10:00:00Z', kind: 'purchase', amount: 70, note: null, userName: null },
    ],
    total: 2,
    page: 1,
    pageSize: 20,
  }

  const openDetails = (balance: number) => {
    open('/suppliers/1', ...ADMIN)
    api.routes['GET /api/v1/suppliers/1'] = () => json(supplier(1, 'Acme', { balance }))
    api.routes['GET /api/v1/suppliers/1/debt-transactions'] = () => json(ledger)
  }

  it('shows what the shop owes and the ledger, newest first, signed', async () => {
    openDetails(40)

    expect(await screen.findByRole('heading', { name: 'Acme' })).toBeInTheDocument()
    expect(await screen.findByText('Part payment')).toBeInTheDocument()
    expect(screen.getByText(/You owe: .*40\.00/)).toBeInTheDocument()
    expect(screen.getByText('Payment Due')).toBeInTheDocument()
    const rows = screen.getAllByRole('row')
    expect(within(rows[1]!).getByText(/30\.00/)).toHaveAttribute('data-sign', 'owed')
    expect(within(rows[2]!).getByText(/\+.*70\.00/)).toHaveAttribute('data-sign', 'owe')
  })

  it('says the supplier owes the shop when the balance is negative', async () => {
    openDetails(-25)

    expect(await screen.findByText(/They owe you: .*25\.00/)).toBeInTheDocument()
    expect(screen.getByText('Receivable')).toBeInTheDocument()
  })

  it('shows the balanced state at zero', async () => {
    openDetails(0)

    expect(await screen.findByText('Balanced - No debt')).toBeInTheDocument()
  })

  it('shows the not-found page for a supplier that does not exist', async () => {
    open('/suppliers/99', ...ADMIN)
    api.routes['GET /api/v1/suppliers/99'] = () => problem(404, 'not_found')
    api.routes['GET /api/v1/suppliers/99/debt-transactions'] = () => problem(404, 'not_found')

    expect(await screen.findByText(/not found|doesn't exist|no longer/i)).toBeInTheDocument()
  })
})
