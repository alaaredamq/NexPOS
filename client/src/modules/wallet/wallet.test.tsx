import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const wallet = (overrides = {}) => ({
  id: 3,
  name: 'Whish',
  provider: 'Whish',
  accountNumber: '70 123 456',
  paymentMethodId: 9,
  paymentMethodName: 'Whish',
  isActive: true,
  balances: [
    { currencyId: 1, currencyCode: 'USD', currencyNotation: '$', balance: 42.5, uncheckedCount: 2, uncheckedAmount: 42.5 },
    { currencyId: 2, currencyCode: 'LBP', currencyNotation: 'LL', balance: 0, uncheckedCount: 0, uncheckedAmount: 0 },
  ],
  ...overrides,
})

const row = (overrides = {}) => ({
  id: 70,
  type: 'Sale',
  amount: 12.5,
  currencyId: 1,
  currencyNotation: '$',
  reference: 'WH-998877',
  notes: null,
  orderId: 5,
  receiptNumber: 'RCP-0005',
  supplierId: null,
  supplierName: null,
  userName: 'Sara Cashier',
  occurredAt: '2026-11-21T09:30:00Z',
  isChecked: false,
  checkedAt: null,
  ...overrides,
})

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'wallet')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(wallets: unknown[], ...permissions: string[]) {
  api = fakeApi(sessionWith(...(permissions.length ? permissions : [Permissions.PaymentMethods_View, Permissions.PaymentMethods_Manage])))
  api.routes['GET /api/v1/wallets'] = () => json(wallets)
  api.routes['GET /api/v1/wallets/3/transactions'] = () =>
    json({ items: [row(), row({ id: 71, type: 'Withdrawal', amount: -30, reference: null, receiptNumber: null, isChecked: true })], total: 2, page: 1, pageSize: 25 })
  return mountModule('/wallet', routes)
}

describe('Whish wallet page', () => {
  it('offers to turn Whish on when there is no wallet yet', async () => {
    open([])
    api.routes['POST /api/v1/wallets/whish'] = () => json(wallet(), 201)

    expect(await screen.findByRole('heading', { name: 'Take payments with Whish' })).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText(/Your shop's Whish number/), { target: { value: ' 70 123 456 ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Turn on Whish' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url.endsWith('/wallets/whish'))).toBe(true))
    expect(api.calls.find((c) => c.url.endsWith('/wallets/whish'))?.body).toEqual({ accountNumber: '70 123 456' })
  })

  it('without PaymentMethods.Manage says to ask the owner', async () => {
    open([], Permissions.PaymentMethods_View)

    expect(await screen.findByText(/Ask the owner or a manager/)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Turn on Whish' })).not.toBeInTheDocument()
  })

  it('needs PaymentMethods.View', async () => {
    const router = open([wallet()], Permissions.POS_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('shows the balance per currency, the Whish number and the movements', async () => {
    open([wallet()])

    expect(await screen.findByText('Balance USD')).toBeInTheDocument()
    expect(screen.getByText('Balance LBP')).toBeInTheDocument()
    expect(screen.getByText('70 123 456')).toBeInTheDocument()
    expect(await screen.findByText('WH-998877')).toBeInTheDocument()
    expect(screen.getByText('RCP-0005')).toBeInTheDocument()
    expect(screen.getByText('Cash out')).toBeInTheDocument()
  })

  it('ticks one row, or every row shown that is still to check', async () => {
    open([wallet()])
    api.routes['POST /api/v1/wallets/3/transactions/check'] = () => json({ changed: 1 })

    const table = await screen.findByRole('table')
    const saleRow = (await within(table).findByText('WH-998877')).closest('tr')!
    fireEvent.click(within(saleRow).getByRole('button', { name: 'Tick' }))
    await waitFor(() => expect(api.calls.some((c) => c.url.endsWith('/transactions/check'))).toBe(true))
    expect(api.calls.find((c) => c.url.endsWith('/transactions/check'))?.body).toEqual({ ids: [70], checked: true })

    fireEvent.click(screen.getByRole('button', { name: 'Tick all shown' }))
    await waitFor(() => expect(api.calls.filter((c) => c.url.endsWith('/transactions/check'))).toHaveLength(2))
    expect(api.calls.filter((c) => c.url.endsWith('/transactions/check'))[1]?.body).toEqual({ ids: [70], checked: true })
  })

  it('records money taken out with a positive amount; the type gives the direction', async () => {
    open([wallet()])
    api.routes['POST /api/v1/wallets/3/transactions'] = () => json(row({ id: 80, type: 'Withdrawal', amount: -20 }), 201)

    fireEvent.click(await screen.findByRole('button', { name: 'Record money in or out' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.change(within(dialog).getByLabelText(/^Amount/), { target: { value: '20' } })
    fireEvent.change(within(dialog).getByLabelText('Notes (optional)'), { target: { value: 'Cashed out at the agent' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url.endsWith('/wallets/3/transactions'))).toBe(true))
    expect(api.calls.find((c) => c.method === 'POST' && c.url.endsWith('/wallets/3/transactions'))?.body).toMatchObject({
      type: 'Withdrawal',
      amount: 20,
      currencyId: 1,
      notes: 'Cashed out at the agent',
      reference: null,
      date: null,
    })
  })

  it('hides recording and ticking from someone who may only look', async () => {
    open([wallet()], Permissions.PaymentMethods_View)

    expect(await screen.findByText('WH-998877')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Record money in or out' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Tick' })).not.toBeInTheDocument()
  })
})
