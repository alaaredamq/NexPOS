import { QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeAll, beforeEach, describe, expect, it } from 'vitest'
import '@/core/i18n'
import { loadNamespaces } from '@/core/i18n'
import { queryClient, queryKeys } from '@/api/queryClient'
import { makeSession } from '@/test/sessionFixture'
import { UiHost } from '@/ui/UiHost'
import { toast } from '@/ui/toast'
import { fakeApi, json, stubMatchMedia, type FakeApi } from '../../_shared/testApp'
import { resetCart, useCartStore, type PosProduct } from '../store/cartStore'
import { OnlineOrdersButton } from './OnlineOrdersButton'
import type { OnlineOrder } from './api'

const shawarma: PosProduct = {
  id: 42, name: 'Shawarma', barcode: '0042', categoryId: 1, categoryName: 'Wraps', currencyId: 1, currencyNotation: '$', sellingPrice: 6,
  supplierDiscountRate: 0, stockQuantity: 20, unit: null, unitKind: 'count', isPack: false, canChangePrice: true,
}

const order = (overrides: Partial<OnlineOrder> = {}): OnlineOrder => ({
  id: 7, number: 104, status: 'new', branch: null, orderType: 'Dine in', name: 'Maya', phone: null, place: 'Table 2', note: 'No garlic',
  createdAtUtc: '2026-11-17T18:00:00Z', total: '$18',
  lines: [
    { name: 'Shawarma', quantity: 3, unitPrice: 6, currency: '$', posProductId: 42, total: '$18' },
    { name: 'House lemonade', quantity: 1, unitPrice: 3, currency: '$', posProductId: null, total: '$3' },
  ],
  ...overrides,
})

let api: FakeApi

function setup(orders: OnlineOrder[], connected = true, permissions: string[] = []) {
  api = fakeApi()
  api.routes['GET /api/v1/online-orders'] = () => json({ connected, reachable: connected, orders })
  api.routes['GET /api/v1/pos/products/42'] = () => json(shawarma)
  api.routes['GET /api/v1/pos/currencies'] = () => json([{ id: 1, code: 'USD', name: 'US Dollar', notation: '$', rateToUsd: 1, isDefault: true }])
  api.routes['PUT /api/v1/online-orders/7/status'] = () => new Response(null, { status: 204 })
  queryClient.clear()
  queryClient.setQueryData(queryKeys.session, makeSession({ permissions }))
  render(
    <QueryClientProvider client={queryClient}>
      <OnlineOrdersButton />
      <UiHost />
    </QueryClientProvider>,
  )
}

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'pos')
})

beforeEach(() => {
  localStorage.clear()
  toast.clear()
  resetCart(null)
})

describe('online orders at the till', () => {
  it('stays out of the way on a PC that is not connected to Online', async () => {
    setup([], false)
    await waitFor(() => expect(api.calls.some((c) => c.url === '/api/v1/online-orders')).toBe(true))
    expect(screen.queryByRole('button', { name: /online orders/i })).toBeNull()
  })

  it('announces a new order with a toast that opens the orders when clicked', async () => {
    setup([])
    await screen.findByRole('button', { name: /online orders/i })
    api.routes['GET /api/v1/online-orders'] = () => json({ connected: true, reachable: true, orders: [order()] })
    await queryClient.invalidateQueries({ queryKey: ['pos', 'online-orders'] })

    fireEvent.click(await screen.findByRole('button', { name: 'New online order' }))

    expect(await screen.findByText('Maya')).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'New online order' })).toBeNull() // the toast closes once used
  })

  it('loads an order into the sale with the till products, notes whose it is, and tells the guest it was accepted', async () => {
    setup([order()])
    fireEvent.click(await screen.findByRole('button', { name: /online orders/i }))
    expect(await screen.findByText('Maya')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Load into sale' }))

    await waitFor(() => expect(useCartStore.getState().lines).toHaveLength(1))
    const line = useCartStore.getState().lines[0]!
    expect(line.productId).toBe(42)
    expect(line.quantity).toBe(3)
    expect(useCartStore.getState().notes).toContain('Online order #104')
    expect(useCartStore.getState().notes).toContain('Table 2')
    const moved = api.calls.find((c) => c.method === 'PUT' && c.url === '/api/v1/online-orders/7/status')
    expect(moved?.body).toEqual({ status: 'accepted' })
    expect(useCartStore.getState().customerId).toBeNull() // no phone: nothing asked, a walk-in
  })

  it('suggests the till customer with the same phone, ticked, and attaches them to the sale', async () => {
    setup([order({ phone: '+961 70 123 456' })])
    api.routes['GET /api/v1/pos/customers/by-phone'] = () => json([{ id: 9, name: 'Maya Saab', phoneNumber: '70123456', balance: 25 }])
    fireEvent.click(await screen.findByRole('button', { name: /online orders/i }))
    fireEvent.click(await screen.findByRole('button', { name: 'Load into sale' }))

    const match = (await screen.findByRole('radio', { name: /Maya Saab/ })) as HTMLInputElement
    expect(match.checked).toBe(true)
    expect(screen.getByText('Owes 25')).toBeTruthy() // a debt is shown, never charged by this
    fireEvent.click(screen.getByRole('button', { name: 'Load into sale' }))

    await waitFor(() => expect(useCartStore.getState().customerId).toBe(9))
    expect(api.calls.some((c) => c.method === 'POST' && c.url === '/api/v1/pos/customers')).toBe(false)
  })

  it('with no match, stays a walk-in unless the cashier chooses to save a new customer', async () => {
    setup([order({ phone: '03 555 120', name: 'Rami Haddad' })], true, ['Customers.Create'])
    api.routes['GET /api/v1/pos/customers/by-phone'] = () => json([])
    api.routes['POST /api/v1/pos/customers'] = () => json({ id: 31, name: 'Rami Haddad', balance: 0 }, 201)
    fireEvent.click(await screen.findByRole('button', { name: /online orders/i }))
    fireEvent.click(await screen.findByRole('button', { name: 'Load into sale' }))

    expect(((await screen.findByRole('radio', { name: /Walk-in/i })) as HTMLInputElement).checked).toBe(true)
    fireEvent.click(screen.getByRole('radio', { name: /Save as a new customer/ }))
    expect((screen.getByLabelText('First name') as HTMLInputElement).value).toBe('Rami')
    expect((screen.getByLabelText('Last name') as HTMLInputElement).value).toBe('Haddad')
    fireEvent.click(screen.getByRole('button', { name: 'Load into sale' }))

    await waitFor(() => expect(useCartStore.getState().customerId).toBe(31))
    const created = api.calls.find((c) => c.method === 'POST' && c.url === '/api/v1/pos/customers')
    expect(created?.body).toEqual({ firstName: 'Rami', lastName: 'Haddad', phoneNumber: '03 555 120' })
  })
})
