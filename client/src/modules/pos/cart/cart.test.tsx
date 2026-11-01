import { QueryClientProvider } from '@tanstack/react-query'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { queryClient } from '@/api/queryClient'
import { UiHost } from '@/ui/UiHost'
import { toast } from '@/ui/toast'
import { fakeApi, json, problem, stubMatchMedia, type FakeApi } from '../../_shared/testApp'
import { resetCart, useCartStore, type PosProduct } from '../store/cartStore'
import { addToCart } from './addToCart'
import { CartPanel } from './CartPanel'
import { useCartUi } from './cartUi'

const USD = { id: 1, code: 'USD', name: 'US Dollar', notation: '$', rateToUsd: 1, isDefault: true }
const LBP = { id: 2, code: 'LBP', name: 'Lebanese Pound', notation: 'LBP', rateToUsd: 89500, isDefault: false }

const product = (id: number, overrides: Partial<PosProduct> = {}): PosProduct => ({
  id,
  name: `Product ${id}`,
  barcode: `000${id}`,
  categoryId: 1,
  categoryName: 'Drinks',
  currencyId: 1,
  currencyNotation: '$',
  sellingPrice: 2,
  supplierDiscountRate: 0,
  stockQuantity: 10,
  unit: null,
  unitKind: 'count',
  isPack: false,
  canChangePrice: true,
  ...overrides,
})

const cola = product(1, { name: 'Cola', sellingPrice: 2 })
const cheese = product(2, { name: 'Cheese', unit: 'kg', unitKind: 'weight', sellingPrice: 10 })

/** A tiny pricer: default price × quantity (or weight). Enough to see what the screen sends and what it shows from the answer. */
function priceHandler(api: FakeApi, products: PosProduct[], extra: { totalDelta?: number } = {}) {
  api.routes['POST /api/v1/pos/price'] = (_init, _url) => {
    const body = api.calls.at(-1)!.body as { currencyId: number; lines: { productId: number; quantity: number; weightOrVolume?: number; priceTierId?: number }[]; discountPercentage?: number }
    const rate = body.currencyId === 2 ? 89500 : 1
    const lines = body.lines.map((l) => {
      const p = products.find((x) => x.id === l.productId)!
      const unit = l.priceTierId ? 1.5 * rate : p.sellingPrice * rate
      return {
        productId: p.id,
        name: p.name,
        quantity: l.quantity,
        weightOrVolume: l.weightOrVolume ?? null,
        priceTierId: l.priceTierId ?? null,
        priceLabel: l.priceTierId ? 'Wholesale' : null,
        isCustomPrice: false,
        unitPrice: unit,
        lineTotal: unit * (l.weightOrVolume ?? l.quantity),
        minUnitPrice: 1 * rate,
        stockQuantity: 10,
        insufficientStock: false,
      }
    })
    const subtotal = lines.reduce((s, l) => s + l.lineTotal, 0)
    const discount = body.discountPercentage ? (subtotal * body.discountPercentage) / 100 : 0
    return json({
      currencyId: body.currencyId,
      currencyNotation: body.currencyId === 2 ? 'LBP' : '$',
      lines,
      subtotal,
      discountAmount: discount,
      discountPercentage: body.discountPercentage ?? 0,
      taxAmount: 0,
      total: subtotal - discount + (extra.totalDelta ?? 0),
      hasInsufficientStock: false,
    })
  }
}

let api: FakeApi
const priceBodies = () => api.calls.filter((c) => c.method === 'POST' && c.url === '/api/v1/pos/price').map((c) => c.body as Record<string, unknown>)
const lastPriceBody = () => priceBodies().at(-1)

function setup(products: PosProduct[] = [cola, cheese]) {
  api = fakeApi()
  api.routes['GET /api/v1/pos/currencies'] = () => json([USD, LBP])
  products.forEach((p) => (api.routes[`GET /api/v1/pos/products/${p.id}`] = () => json(p)))
  priceHandler(api, products)
  const onCheckout = vi.fn()
  queryClient.clear()
  render(
    <QueryClientProvider client={queryClient}>
      <CartPanel onCheckout={onCheckout} />
      <UiHost />
    </QueryClientProvider>,
  )
  return { onCheckout }
}

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'pos')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  resetCart(null)
  useCartUi.setState({ dialog: null, amountRequest: null })
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

const total = () => screen.getByTestId('cart-total')

describe('cart panel', () => {
  it('starts empty with checkout disabled', async () => {
    setup()
    expect(await screen.findByText('Cart is Empty')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Checkout' })).toBeDisabled()
  })

  it('shows the server total and only lets the cashier check out once the server has priced the cart', async () => {
    const { onCheckout } = setup()
    await screen.findByText('Cart is Empty')
    // the server will say 9.99 for 3 colas (the local arithmetic says 6.00): the screen must follow the server
    priceHandler(api, [cola, cheese], { totalDelta: 3.99 })

    await act(async () => void (await addToCart(cola, { quantity: 3 })))
    expect(await screen.findByText('Cola')).toBeInTheDocument()
    // estimate first, checkout locked
    expect(screen.getByRole('button', { name: 'Checkout' })).toBeDisabled()

    await waitFor(() => expect(total()).toHaveTextContent('9.99'))
    const checkout = screen.getByRole('button', { name: 'Checkout' })
    expect(checkout).toBeEnabled()
    fireEvent.click(checkout)
    expect(onCheckout).toHaveBeenCalledTimes(1)

    expect(lastPriceBody()).toEqual({ currencyId: 1, lines: [{ productId: 1, quantity: 3 }] })
  })

  it('debounces: a burst of changes sends one price call for the final cart', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    await act(async () => {
      await addToCart(cola)
      await addToCart(cola)
      await addToCart(cola)
    })
    await waitFor(() => expect(priceBodies().length).toBeGreaterThan(0))
    await waitFor(() => expect(total()).toHaveTextContent('6.00'))
    expect(priceBodies()).toHaveLength(1)
    expect(lastPriceBody()).toEqual({ currencyId: 1, lines: [{ productId: 1, quantity: 3 }] })
  })

  it('locks checkout and shows the server message when the cart cannot be priced', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    api.routes['POST /api/v1/pos/price'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'price_below_minimum', title: 'Price below minimum', detail: 'Cola is priced below cost.', traceId: 't' }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    await act(async () => void (await addToCart(cola)))
    expect(await screen.findByText('Cola is priced below cost.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Checkout' })).toBeDisabled()
  })

  it('changes pieces with the stepper and the typed number, and removing the last piece removes the line', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    await act(async () => void (await addToCart(cola)))
    fireEvent.click(await screen.findByRole('button', { name: 'Increase quantity of Cola' }))
    expect(useCartStore.getState().lines[0]?.quantity).toBe(2)

    const input = screen.getByRole('textbox', { name: 'Quantity' })
    fireEvent.change(input, { target: { value: '12' } })
    fireEvent.keyDown(input, { key: 'Enter' })
    expect(useCartStore.getState().lines[0]?.quantity).toBe(12)

    fireEvent.change(input, { target: { value: '0' } })
    fireEvent.blur(input)
    expect(useCartStore.getState().lines).toHaveLength(0)
    // …and undo brings it back
    fireEvent.click(screen.getByRole('button', { name: 'Undo' }))
    expect(useCartStore.getState().lines[0]?.quantity).toBe(12)
  })

  it('asks for a weight, converts grams to the product unit and sells that', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    let added: Promise<boolean> = Promise.resolve(false)
    act(() => {
      added = addToCart(cheese)
    })
    const dialog = await screen.findByRole('dialog', { name: 'Cheese' })
    // the prompt starts on grams and shows the price per kg
    expect(within(dialog).getByRole('combobox', { name: 'Unit' })).toHaveValue('g')
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Weight' }), { target: { value: '300' } })
    expect(within(dialog).getByText(/\$ 3\.00/)).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add to cart' }))
    expect(await added).toBe(true)

    expect(useCartStore.getState().lines[0]).toMatchObject({ weightOrVolume: 0.3, displayValue: 300, displayUnit: 'g' })
    await waitFor(() => expect(lastPriceBody()).toEqual({ currencyId: 1, lines: [{ productId: 2, quantity: 1, weightOrVolume: 0.3 }] }))
  })

  it('adds nothing when the weight prompt is cancelled', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    let added: Promise<boolean> = Promise.resolve(true)
    act(() => {
      added = addToCart(cheese)
    })
    const dialog = await screen.findByRole('dialog', { name: 'Cheese' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    expect(await added).toBe(false)
    expect(useCartStore.getState().lines).toHaveLength(0)
  })

  it('lets the cashier pick a stored price, which is then sent by id', async () => {
    setup()
    api.routes['GET /api/v1/pos/products/1/prices'] = () =>
      json({ productId: 1, currencyId: 1, minPrice: 1, hasCost: true, options: [{ id: null, name: '', price: 2 }, { id: 7, name: 'Wholesale', price: 1.5 }] })
    await screen.findByText('Cart is Empty')
    await act(async () => void (await addToCart(cola)))

    fireEvent.click(await screen.findByRole('button', { name: /Change the price of Cola/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Cola' })
    fireEvent.click(await within(dialog).findByRole('button', { name: /Wholesale/ }))

    expect(useCartStore.getState().lines[0]?.price).toMatchObject({ kind: 'tier', tierId: 7, label: 'Wholesale' })
    await waitFor(() => expect(lastPriceBody()).toMatchObject({ lines: [{ productId: 1, quantity: 1, priceTierId: 7 }] }))
  })

  it('refuses a custom price below the minimum and accepts one above it', async () => {
    setup()
    api.routes['GET /api/v1/pos/products/1/prices'] = () =>
      json({ productId: 1, currencyId: 1, minPrice: 1, hasCost: true, options: [{ id: null, name: '', price: 2 }] })
    await screen.findByText('Cart is Empty')
    await act(async () => void (await addToCart(cola)))
    fireEvent.click(await screen.findByRole('button', { name: /Change the price of Cola/ }))
    const dialog = await screen.findByRole('dialog', { name: 'Cola' })

    const input = await within(dialog).findByRole('textbox', { name: 'Custom price' })
    fireEvent.change(input, { target: { value: '0.5' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Use price' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('below the minimum')
    expect(useCartStore.getState().lines[0]?.price).toEqual({ kind: 'default' })

    fireEvent.change(input, { target: { value: '1.25' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Use price' }))
    await waitFor(() => expect(useCartStore.getState().lines[0]?.price).toEqual({ kind: 'custom', amount: 1.25, currencyId: 1 }))
    await waitFor(() => expect(lastPriceBody()).toMatchObject({ lines: [{ productId: 1, quantity: 1, customPrice: 1.25 }] }))
  })

  it('applies a percentage discount and sends it to the pricer', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    api.routes['GET /api/v1/pos/discounts'] = () => json([])
    await act(async () => void (await addToCart(cola, { quantity: 5 })))
    await waitFor(() => expect(total()).toHaveTextContent('10.00'))

    fireEvent.click(screen.getByRole('button', { name: 'Discount' }))
    const dialog = await screen.findByRole('dialog', { name: 'Apply Discount' })
    fireEvent.change(within(dialog).getByRole('textbox', { name: 'Percentage (%)' }), { target: { value: '10' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply' }))

    await waitFor(() => expect(lastPriceBody()).toMatchObject({ discountPercentage: 10 }))
    await waitFor(() => expect(screen.getByRole('button', { name: 'Checkout' })).toBeEnabled())
    expect(total()).toHaveTextContent('9.00')
  })

  it('refuses a flat discount larger than the subtotal', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    api.routes['GET /api/v1/pos/discounts'] = () => json([])
    await act(async () => void (await addToCart(cola)))
    await waitFor(() => expect(total()).toHaveTextContent('2.00'))

    fireEvent.click(screen.getByRole('button', { name: 'Discount' }))
    const dialog = await screen.findByRole('dialog', { name: 'Apply Discount' })
    fireEvent.change(within(dialog).getByRole('combobox', { name: 'Discount Type' }), { target: { value: 'amount' } })
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Fixed Amount/ }), { target: { value: '5' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Apply' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Discount cannot exceed total amount')
    expect(useCartStore.getState().discount).toBeNull()
  })

  it('prices the cart in the other currency when the cashier toggles it', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    await act(async () => void (await addToCart(cola)))
    await waitFor(() => expect(total()).toHaveTextContent('2.00'))
    // the other currency is shown as an estimate under the total
    expect(screen.getByText(/≈/)).toHaveTextContent('LBP 179,000')

    fireEvent.click(screen.getByRole('button', { name: /Cart currency/ }))
    await waitFor(() => expect(lastPriceBody()).toMatchObject({ currencyId: 2 }))
    await waitFor(() => expect(total()).toHaveTextContent('LBP 179,000'))
  })

  it('holds the cart with its notes and empties the till', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    api.routes['POST /api/v1/carts'] = () => json({ id: 9, customerId: null, customerName: '', isWalkIn: true, totalAmount: 2, itemCount: 1, createdAt: '2026-11-01T10:00:00Z', notes: 'back soon' })
    await act(async () => void (await addToCart(cola)))
    await waitFor(() => expect(total()).toHaveTextContent('2.00'))

    fireEvent.click(screen.getByRole('button', { name: 'Hold' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hold Cart' })
    fireEvent.change(within(dialog).getByRole('textbox', { name: /Notes/ }), { target: { value: 'back soon' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Hold Cart' }))

    await waitFor(() => expect(useCartStore.getState().lines).toHaveLength(0))
    const hold = api.calls.find((c) => c.method === 'POST' && c.url === '/api/v1/carts')
    expect(hold?.body).toEqual({ cart: { currencyId: 1, lines: [{ productId: 1, quantity: 1 }] }, customerId: null, notes: 'back soon' })
  })

  it('does not empty the till when the server refuses to park the cart', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    api.routes['POST /api/v1/carts'] = () => problem(400, 'price_below_minimum', 'Price below minimum')
    await act(async () => void (await addToCart(cola)))
    await waitFor(() => expect(total()).toHaveTextContent('2.00'))
    fireEvent.click(screen.getByRole('button', { name: 'Hold' }))
    const dialog = await screen.findByRole('dialog', { name: 'Hold Cart' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Hold Cart' }))
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Price below minimum')
    expect(useCartStore.getState().lines).toHaveLength(1)
  })
})

describe('held carts', () => {
  const held = { id: 5, customerId: 3, customerName: 'Rana', isWalkIn: false, totalAmount: 4.5, itemCount: 2, createdAt: '2026-11-01T09:00:00Z', notes: 'pick up at 5' }
  const resumed = {
    cartId: 5,
    customerId: 3,
    notes: 'pick up at 5',
    discountPercentage: 10,
    discountAmount: null,
    discountLabel: null,
    lines: [
      { productId: 1, name: 'Cola', quantity: 2, weightOrVolume: null, priceKind: 'tier', priceTierId: 7, priceLabel: 'Wholesale', unitPrice: 1.5, currencyId: 1 },
      { productId: 2, name: 'Cheese', quantity: 1, weightOrVolume: 0.25, priceKind: 'default', priceTierId: null, priceLabel: null, unitPrice: 10, currencyId: 1 },
      { productId: 99, name: 'Gone', quantity: 1, weightOrVolume: null, priceKind: 'default', priceTierId: null, priceLabel: null, unitPrice: 1, currencyId: 1 },
    ],
  }

  it('puts a held cart back with its prices, discount and customer, leaving out a product that no longer exists', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    api.routes['GET /api/v1/carts'] = () => json([held])
    api.routes['GET /api/v1/carts/5'] = () => json(resumed)

    fireEvent.click(screen.getByRole('button', { name: 'Retrieve' }))
    const dialog = await screen.findByRole('dialog', { name: 'Held Carts' })
    fireEvent.click(await within(dialog).findByRole('button', { name: /Rana/ }))

    await waitFor(() => expect(useCartStore.getState().lines).toHaveLength(2))
    const state = useCartStore.getState()
    expect(state.heldCartId).toBe(5)
    expect(state.customerId).toBe(3)
    expect(state.notes).toBe('pick up at 5')
    expect(state.discount).toMatchObject({ type: 'percentage', value: 10 })
    expect(state.lines[0]).toMatchObject({ productId: 1, quantity: 2, price: { kind: 'tier', tierId: 7, label: 'Wholesale', amount: 1.5 } })
    expect(state.lines[1]).toMatchObject({ productId: 2, weightOrVolume: 0.25, displayUnit: 'kg' })
    expect(await screen.findByText(/Gone is no longer available/)).toBeInTheDocument()
    // and the screen asks the server to re-price it
    await waitFor(() => expect(lastPriceBody()).toMatchObject({ discountPercentage: 10, lines: [{ productId: 1, quantity: 2, priceTierId: 7 }, { productId: 2, weightOrVolume: 0.25 }] }))
  })

  it('offers to park the current cart first when the till is not empty', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    api.routes['GET /api/v1/carts'] = () => json([held])
    api.routes['GET /api/v1/carts/5'] = () => json({ ...resumed, lines: [resumed.lines[0]], discountPercentage: null })
    api.routes['POST /api/v1/carts'] = () => json({ ...held, id: 6 })
    act(() => useCartStore.getState().addProduct(cola))

    fireEvent.click(screen.getByRole('button', { name: 'Retrieve' }))
    const dialog = await screen.findByRole('dialog', { name: 'Held Carts' })
    fireEvent.click(await within(dialog).findByRole('button', { name: /Rana/ }))
    const ask = await screen.findByRole('dialog', { name: 'Save Current Cart?' })
    fireEvent.click(within(ask).getByRole('button', { name: 'Yes, Save Current' }))

    await waitFor(() => expect(useCartStore.getState().heldCartId).toBe(5))
    const saved = api.calls.find((c) => c.method === 'POST' && c.url === '/api/v1/carts')
    expect(saved?.body).toMatchObject({ customerId: null, notes: 'Auto-saved before loading another cart' })
  })

  it('deletes a held cart after confirming', async () => {
    setup()
    await screen.findByText('Cart is Empty')
    let list = [held]
    api.routes['GET /api/v1/carts'] = () => json(list)
    api.routes['DELETE /api/v1/carts/5'] = () => {
      list = []
      return new Response(null, { status: 204 })
    }
    fireEvent.click(screen.getByRole('button', { name: 'Retrieve' }))
    const dialog = await screen.findByRole('dialog', { name: 'Held Carts' })
    fireEvent.click(await within(dialog).findByRole('button', { name: 'Delete Held Cart?' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Yes, Delete' }))
    expect(await within(dialog).findByText('No held carts found')).toBeInTheDocument()
  })
})
