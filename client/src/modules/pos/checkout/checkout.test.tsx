import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { makeSession } from '@/test/sessionFixture'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, stubMatchMedia, type FakeApi } from '../../_shared/testApp'
import routes from '../routes'
import { posBus } from '../slots'
import { resetCart, useCartStore } from '../store/cartStore'
import { useCheckoutUi } from './checkoutUi'
import { forgetAttempt } from './tender'

const COLA = {
  id: 1,
  name: 'Cola',
  barcode: '6001',
  categoryId: 1,
  categoryName: 'Drinks',
  currencyId: 1,
  currencyNotation: '$',
  sellingPrice: 2,
  supplierDiscountRate: 0,
  stockQuantity: 40,
  minimumStock: null,
  unit: null,
  unitKind: 'count',
  isPack: false,
  packSize: null,
  singleProductId: null,
  canChangePrice: true,
}
const USD = { id: 1, code: 'USD', name: 'US Dollar', notation: '$', rateToUsd: 1, isDefault: true }
const METHODS = [
  { id: 10, name: 'Cash', isDefault: true, requiresChange: true },
  { id: 11, name: 'Card', isDefault: false, requiresChange: false },
]

interface CheckoutBody {
  idempotencyKey: string
  cart: { currencyId: number; lines: { productId: number; quantity: number }[] }
  customerId: number | null
  paymentMethodId: number
  addToDebt: boolean
  totalAmount: number
  allowInsufficientStock: boolean
  notes: string | null
}

let api: FakeApi

const saleFor = (body: CheckoutBody, overrides: Record<string, unknown> = {}) => ({
  orderId: 77,
  orderNumber: 'ORD-000077',
  receiptId: 5,
  receiptNumber: 'RCP-0005',
  orderDate: '2026-11-01T10:00:00Z',
  currencyId: 1,
  currencyNotation: '$',
  subtotal: 6,
  discountAmount: 0,
  taxAmount: 0,
  total: body.totalAmount,
  paid: body.totalAmount,
  change: 0,
  paymentMethod: body.addToDebt ? 'Customer Debt' : 'Cash',
  customerId: body.customerId,
  customerName: body.customerId ? 'Rana Haddad' : null,
  addedToDebt: body.addToDebt,
  replayed: false,
  receiptPrinted: true,
  lines: [{ productId: 1, name: 'Cola', quantity: 3, unitPrice: 2, lineTotal: 6 }],
  ...overrides,
})

const bodies = () => api.calls.filter((c) => c.method === 'POST' && c.url === '/api/v1/pos/sales/checkout').map((c) => c.body as CheckoutBody)

function open(session = makeSession({ permissions: ['Orders.View', 'Customers.Create'] })) {
  api = fakeApi(session)
  api.routes['GET /api/v1/pos/categories'] = () => json([{ id: 1, name: 'Drinks', productCount: 1 }])
  api.routes['GET /api/v1/pos/products'] = () => json({ items: [COLA], total: 1, page: 1, pageSize: 120, isMainScreen: true })
  api.routes['GET /api/v1/pos/products/1'] = () => json(COLA)
  api.routes['GET /api/v1/pos/currencies'] = () => json([USD])
  api.routes['GET /api/v1/pos/payment-methods'] = () => json(METHODS)
  api.routes['GET /api/v1/pos/customers'] = () => json({ items: [{ id: 3, name: 'Rana Haddad', email: null, balance: 12 }], page: 1, pageSize: 30, hasMore: false })
  api.routes['POST /api/v1/pos/price'] = () => {
    const body = api.calls.at(-1)!.body as { currencyId: number; lines: { productId: number; quantity: number }[] }
    const quantity = body.lines[0]!.quantity
    return json({
      currencyId: 1,
      currencyNotation: '$',
      lines: [
        { productId: 1, name: 'Cola', quantity, isCustomPrice: false, unitPrice: 2, lineTotal: 2 * quantity, minUnitPrice: 1, stockQuantity: 40, insufficientStock: false },
      ],
      subtotal: 2 * quantity,
      discountAmount: 0,
      discountPercentage: 0,
      taxAmount: 0,
      total: 2 * quantity,
      hasInsufficientStock: false,
    })
  }
  api.routes['POST /api/v1/pos/sales/checkout'] = () => json(saleFor(bodies().at(-1)!))
  return mountModule('/pos', routes)
}

/** Puts three colas on the cart through the tile and waits for the server's price (6.00). */
async function fillCart(quantity = 3) {
  const tile = await screen.findByRole('button', { name: /Cola/ }, { timeout: 8000 })
  for (let i = 0; i < quantity; i++) fireEvent.click(tile)
  const checkout = screen.getByRole('button', { name: 'Checkout' })
  await waitFor(() => expect(screen.getByTestId('cart-total')).toHaveTextContent(String(2 * quantity)))
  await waitFor(() => expect(checkout).toBeEnabled())
  return checkout
}

/** Presses Checkout and waits until the dialog has its payment methods. */
async function pay() {
  fireEvent.click(await fillCart())
  await screen.findByRole('radio', { name: 'Cash' })
}

/** Text with the bidi isolates the money formatter wraps numbers in removed. */
const plain = (el: HTMLElement) => (el.textContent ?? '').replace(/[⁦⁩]/g, '')

const dialog = () => screen.getAllByRole('dialog')[0]!
const complete = () => within(dialog()).getByRole('button', { name: 'Complete' })

beforeAll(async () => {
  stubMatchMedia()
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 600 })
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 800 })
  await loadNamespaces('common', 'pos', 'print')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
  resetCart(null)
  forgetAttempt()
  useCheckoutUi.setState({ dialog: null })
})

afterEach(() => vi.unstubAllGlobals())

describe('checkout', () => {
  it('sells the cart the server priced, with its total and a key, then clears the till', async () => {
    open()
    await pay()

    expect(within(dialog()).getByText('Total due')).toBeInTheDocument()
    expect(dialog()).toHaveTextContent('$ 6.00')
    // the default method is already chosen
    expect(within(dialog()).getByRole('radio', { name: 'Cash' })).toHaveAttribute('aria-checked', 'true')

    fireEvent.click(complete())

    await screen.findByText('Sale Complete!')
    const [body] = bodies()
    expect(body).toMatchObject({
      cart: { currencyId: 1, lines: [{ productId: 1, quantity: 3 }] },
      customerId: null,
      paymentMethodId: 10,
      addToDebt: false,
      totalAmount: 6,
    })
    expect(body!.idempotencyKey.length).toBeGreaterThanOrEqual(8)
    // never a price in the request: the server prices the cart itself
    expect(JSON.stringify(body!.cart)).not.toMatch(/unitPrice|sellingPrice/)
    expect(useCartStore.getState().lines).toHaveLength(0)
    expect(dialog()).toHaveTextContent('RCP-0005')

    fireEvent.click(within(dialog()).getByRole('button', { name: 'New sale' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('works out the change for cash and refuses less than the total', async () => {
    open()
    await pay()

    const received = within(dialog()).getByLabelText('Amount received')
    fireEvent.change(received, { target: { value: '4' } })
    expect(dialog()).toHaveTextContent('Still to pay')
    expect(complete()).toBeDisabled()

    fireEvent.change(received, { target: { value: '10' } })
    expect(dialog()).toHaveTextContent('Change due')
    expect(dialog()).toHaveTextContent('$ 4.00')
    expect(complete()).toBeEnabled()

    fireEvent.click(complete())
    const done = await screen.findByText('Sale Complete!')
    expect(done).toBeInTheDocument()
    // the change is shown big on the success screen
    expect(dialog()).toHaveTextContent('Change due')
    expect(dialog()).toHaveTextContent('$ 4.00')
  })

  it('does not ask for an amount received on a card payment', async () => {
    open()
    await pay()

    fireEvent.click(within(dialog()).getByRole('radio', { name: 'Card' }))

    expect(within(dialog()).queryByLabelText('Amount received')).not.toBeInTheDocument()
    fireEvent.click(complete())
    await screen.findByText('Sale Complete!')
    expect(bodies()[0]?.paymentMethodId).toBe(11)
  })

  it('treats a lower typed total as a discount and sends it', async () => {
    open()
    await pay()

    fireEvent.change(within(dialog()).getByLabelText('Total Amount (Editable)'), { target: { value: '5.5' } })
    expect(plain(dialog())).toMatch(/Discount \(8\.3%\): -\$ 0\.50/)
    fireEvent.click(complete())

    await screen.findByText('Sale Complete!')
    expect(bodies()[0]?.totalAmount).toBe(5.5)
  })

  it('lets the cashier lower the total as far as they choose', async () => {
    open()
    await pay()

    fireEvent.change(within(dialog()).getByLabelText('Total Amount (Editable)'), { target: { value: '1' } })

    expect(plain(dialog())).toMatch(/Discount \(.*\): -\$ 5\.00/)
    expect(complete()).toBeEnabled()
  })

  it('will not offer to sell for a raised total the server would refuse', async () => {
    open()
    await pay()

    fireEvent.change(within(dialog()).getByLabelText('Total Amount (Editable)'), { target: { value: '9' } })

    expect(plain(dialog())).toMatch(/can be raised above \$ 6\.00 by at most/)
    expect(complete()).toBeDisabled()
  })

  it('puts a sale on the customer account only for a real customer', async () => {
    open()
    await pay()

    const debt = within(dialog()).getByRole('button', { name: 'Add to Debt' })
    expect(debt).toBeDisabled()

    fireEvent.click(within(dialog()).getByRole('combobox', { name: 'Customer (Optional)' }))
    fireEvent.click(await screen.findByRole('option', { name: /Rana Haddad/ }))
    expect(debt).toBeEnabled()
    fireEvent.click(debt)

    await screen.findByText('Sale Complete!')
    expect(bodies()[0]).toMatchObject({ customerId: 3, addToDebt: true, paymentMethodId: 0 })
    expect(dialog()).toHaveTextContent("Added to Rana Haddad's account")
  })

  it('adds a new customer without leaving the dialog and selects them', async () => {
    open()
    api.routes['POST /api/v1/pos/customers'] = () => json({ id: 9, name: 'Omar Said', email: null, balance: 0 }, 201)
    await pay()

    fireEvent.click(within(dialog()).getByRole('button', { name: 'New' }))
    fireEvent.change(within(dialog()).getByLabelText('First name'), { target: { value: 'Omar' } })
    fireEvent.change(within(dialog()).getByLabelText('Last name'), { target: { value: 'Said' } })
    fireEvent.click(within(dialog()).getByRole('button', { name: 'Save and select' }))

    await waitFor(() => expect(within(dialog()).getByRole('combobox', { name: 'Customer (Optional)' })).toHaveValue('Omar Said'))
    expect(useCartStore.getState().customerId).toBe(9)
    expect(api.calls.find((c) => c.url === '/api/v1/pos/customers' && c.method === 'POST')?.body).toEqual({ firstName: 'Omar', lastName: 'Said' })
  })

  it('keeps the cart and says why when the server refuses the sale', async () => {
    open()
    api.routes['POST /api/v1/pos/sales/checkout'] = () => problem(409, 'total_mismatch', 'The total changed.')
    await pay()

    fireEvent.click(complete())

    expect(await within(dialog()).findByRole('alert')).toHaveTextContent('The total changed.')
    expect(useCartStore.getState().lines).toHaveLength(1)
    expect(complete()).toBeEnabled()
  })

  it('retries a lost connection with the same key and sells once', async () => {
    open()
    let first = true
    api.routes['POST /api/v1/pos/sales/checkout'] = () => {
      if (first) {
        first = false
        throw new TypeError('Failed to fetch')
      }
      return json(saleFor(bodies().at(-1)!))
    }
    await pay()

    fireEvent.click(complete())
    expect(await within(dialog()).findByRole('alert')).toHaveTextContent(/could not confirm whether this sale went through/i)
    expect(useCartStore.getState().lines).toHaveLength(1)

    fireEvent.click(within(dialog()).getByRole('button', { name: 'Try again' }))
    await screen.findByText('Sale Complete!')

    const sent = bodies()
    expect(sent).toHaveLength(2)
    expect(sent[1]!.idempotencyKey).toBe(sent[0]!.idempotencyKey)
    expect(useCartStore.getState().lines).toHaveLength(0)
  })

  it('sells an item the shelf says is gone without asking: no prompt, stock allowed to run out', async () => {
    open()
    // the price call reports the shortage; the sale must go through all the same
    api.routes['POST /api/v1/pos/price'] = () => {
      const body = api.calls.at(-1)!.body as { lines: { quantity: number }[] }
      const quantity = body.lines[0]!.quantity
      return json({
        currencyId: 1,
        currencyNotation: '$',
        lines: [{ productId: 1, name: 'Cola', quantity, isCustomPrice: false, unitPrice: 2, lineTotal: 2 * quantity, minUnitPrice: 1, stockQuantity: 0, insufficientStock: true }],
        subtotal: 2 * quantity,
        discountAmount: 0,
        discountPercentage: 0,
        taxAmount: 0,
        total: 2 * quantity,
        hasInsufficientStock: true,
      })
    }
    await pay()

    fireEvent.click(complete())

    await screen.findByText('Sale Complete!')
    expect(screen.queryByText('Not enough stock')).not.toBeInTheDocument()
    expect(bodies()).toHaveLength(1)
    expect(bodies()[0]!.allowInsufficientStock).toBe(true)
  })

  it('shows the sales-stopped screen when the licence stops selling, and keeps the cart', async () => {
    open()
    api.routes['POST /api/v1/pos/sales/checkout'] = () => problem(403, 'license_sales_stopped', 'Sales stopped')
    await pay()

    fireEvent.click(complete())

    expect(await screen.findByText('Sales are stopped')).toBeInTheDocument()
    expect(useCartStore.getState().lines).toHaveLength(1)
  })

  it('warns about a closed drawer and links to opening it, without blocking the sale', async () => {
    open(makeSession({ permissions: ['Orders.View'], drawer: { isOpen: false, openingCash: 0 } }))
    await pay()

    expect(within(dialog()).getByRole('status')).toHaveTextContent('Your cash drawer is closed.')
    expect(within(dialog()).getByRole('link', { name: 'Open drawer' })).toHaveAttribute('href', '/cash-drawer/open')
    expect(complete()).toBeEnabled()
    fireEvent.click(complete())
    await screen.findByText('Sale Complete!')
    expect(bodies()[0]).not.toHaveProperty('requireOpenDrawer', true)
  })

  it('removes a resumed held cart from the held list once it is sold', async () => {
    open()
    api.routes['DELETE /api/v1/carts/42'] = () => new Response(null, { status: 204 })
    await pay()
    useCartStore.setState({ heldCartId: 42 })

    fireEvent.click(complete())
    await screen.findByText('Sale Complete!')

    await waitFor(() => expect(api.calls.some((c) => c.method === 'DELETE' && c.url === '/api/v1/carts/42')).toBe(true))
  })

  it('says the sale was a replay when the server returns the earlier one', async () => {
    open()
    api.routes['POST /api/v1/pos/sales/checkout'] = () => json(saleFor(bodies().at(-1)!, { replayed: true }))
    await pay()

    fireEvent.click(complete())

    expect(await screen.findByText(/Nothing was sold twice/)).toBeInTheDocument()
  })

  it('tells the cashier when the receipt did not print and lets them retry', async () => {
    open()
    api.routes['POST /api/v1/pos/sales/checkout'] = () => json(saleFor(bodies().at(-1)!, { receiptPrinted: false }))
    api.routes['POST /api/v1/pos/sales/77/print'] = () => json({ printed: true, message: 'Printed.', pdfUrl: '/api/v1/pos/sales/77/receipt.pdf' })
    await pay()

    fireEvent.click(complete())
    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be printed/i)

    fireEvent.click(within(dialog()).getByRole('button', { name: 'Try printing again' }))
    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url === '/api/v1/pos/sales/77/print')).toBe(true))
    // The retry worked: the popup has nothing left to say, so it closes.
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('keeps the popup and its warning when the retry still cannot print', async () => {
    open()
    api.routes['POST /api/v1/pos/sales/checkout'] = () => json(saleFor(bodies().at(-1)!, { receiptPrinted: false }))
    api.routes['POST /api/v1/pos/sales/77/print'] = () => json({ printed: false, message: 'Printer unavailable.', pdfUrl: '/api/v1/pos/sales/77/receipt.pdf' })
    await pay()

    fireEvent.click(complete())
    await screen.findByRole('alert')

    fireEvent.click(within(dialog()).getByRole('button', { name: 'Try printing again' }))
    // The "download instead" question is itself a dialog; decline it and the sale-complete popup must still be there.
    fireEvent.click(await screen.findByRole('button', { name: /Cancel|Close|No/i }))
    await waitFor(() => expect(api.calls.filter((c) => c.url === '/api/v1/pos/sales/77/print')).toHaveLength(1))
    expect(await screen.findByText(/could not be printed/i)).toBeInTheDocument()
  })

  it('does nothing but say so when the cart is empty', async () => {
    open()
    await screen.findByRole('button', { name: /Cola/ })

    posBus.run('checkout')

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(await screen.findByText('Cart is empty!')).toBeInTheDocument()
  })
})

describe('last sales', () => {
  it('lists the cashier’s latest sales and reprints one', async () => {
    open()
    api.routes['GET /api/v1/pos/sales/recent'] = () =>
      json([{ orderId: 70, orderNumber: 'ORD-70', receiptId: 3, receiptNumber: 'RCP-0003', orderDate: '2026-11-01T09:00:00Z', total: 12.5, currencyNotation: '$', customerName: 'Rana Haddad' }])
    api.routes['POST /api/v1/pos/sales/70/print'] = () => json({ printed: true, message: 'Printed.', pdfUrl: '/api/v1/pos/sales/70/receipt.pdf' })
    await screen.findByRole('button', { name: /Cola/ })

    fireEvent.click(screen.getByTestId('quick-recent'))

    expect(await within(await screen.findByRole('dialog')).findByText('RCP-0003')).toBeInTheDocument()
    fireEvent.click(within(dialog()).getByRole('button', { name: 'Print receipt' }))
    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url === '/api/v1/pos/sales/70/print')).toBe(true))
  })

  it('hides the button from a cashier who may not see sales', async () => {
    open(makeSession({ permissions: [] }))
    await screen.findByRole('button', { name: /Cola/ })

    expect(screen.queryByTestId('quick-recent')).not.toBeInTheDocument()
  })
})
