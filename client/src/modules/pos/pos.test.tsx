import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'
import { resetCart } from './store/cartStore'
import { posBus, type AddProductRequest } from './slots'

type Overrides = Record<string, unknown>

const product = (id: number, name: string, overrides: Overrides = {}) => ({
  id,
  name,
  barcode: `600${id}`,
  categoryId: 1,
  categoryName: 'Drinks',
  currencyId: 1,
  currencyNotation: '$',
  sellingPrice: 2.5,
  supplierDiscountRate: 0,
  stockQuantity: 40,
  minimumStock: null,
  unit: null,
  unitKind: 'count',
  isPack: false,
  packSize: null,
  singleProductId: null,
  canChangePrice: true,
  ...overrides,
})

const MILK = product(1, 'Milk 1L', { sellingPrice: 1.75 })
const WATER = product(2, 'Water 500ml', { stockQuantity: 3 }) // low (<= 5)
const COLA = product(3, 'Cola', { stockQuantity: 0 }) // out
const BREAD = product(4, 'Bread', { categoryId: 2, categoryName: 'Bakery' })
const PRODUCTS = [MILK, WATER, COLA, BREAD]

const page = (items: unknown[], extra: Overrides = {}) => ({ items, total: items.length, page: 1, pageSize: 120, isMainScreen: true, ...extra })

let api: FakeApi
let added: AddProductRequest[] = []
let unsubscribe = () => {}

beforeAll(async () => {
  stubMatchMedia()
  // The virtual grid measures its scroll box; jsdom has no layout, so give every element a size.
  Object.defineProperty(HTMLElement.prototype, 'clientHeight', { configurable: true, get: () => 600 })
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, get: () => 800 })
  await loadNamespaces('common', 'pos')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
  added = []
  resetCart() // the cart store is module state: start every test with an empty till
  unsubscribe = posBus.onAddProduct((request) => added.push(request))
})

afterEach(() => {
  unsubscribe()
  vi.unstubAllGlobals()
})

function open() {
  api = fakeApi(sessionWith())
  api.routes['GET /api/v1/pos/categories'] = () =>
    json([
      { id: 1, name: 'Drinks', productCount: 3 },
      { id: 2, name: 'Bakery', productCount: 1 },
    ])
  api.routes['GET /api/v1/pos/products'] = (_init, url) => {
    const categoryId = new URL(url, 'http://x').searchParams.get('categoryId')
    return json(page(categoryId ? PRODUCTS.filter((p) => String(p.categoryId) === categoryId) : PRODUCTS))
  }
  api.routes['GET /api/v1/pos/currencies'] = () => json([{ id: 1, code: 'USD', name: 'US Dollar', notation: '$', rateToUsd: 1, isDefault: true }])
  api.routes['GET /api/v1/pos/products/search'] = (_init, url) => {
    const params = new URL(url, 'http://x').searchParams
    const q = (params.get('q') ?? '').toLowerCase()
    if (params.get('barcodeOnly') === 'true') {
      const hit = PRODUCTS.find((p) => p.barcode === q)
      return hit ? json({ searchType: 'barcode', items: [hit], hasMore: false }) : problem(404, 'product_not_found')
    }
    const exact = PRODUCTS.find((p) => p.barcode === q)
    if (exact) return json({ searchType: 'barcode', items: [exact], hasMore: false })
    return json({ searchType: 'name', items: PRODUCTS.filter((p) => p.name.toLowerCase().includes(q)), hasMore: false })
  }
  return mountModule('/pos', routes)
}

const scanBox = () => screen.getByRole('textbox', { name: 'Scan barcode' })

function scan(text: string) {
  fireEvent.change(scanBox(), { target: { value: text } })
  fireEvent.keyDown(scanBox(), { key: 'Enter' })
}

describe('POS catalog', () => {
  it('shows the main-screen tiles with price and a stock state for each', async () => {
    open()

    expect(await screen.findByRole('button', { name: /Milk 1L/ })).toHaveTextContent('$ 1.75')
    expect(screen.getByRole('button', { name: /Water 500ml/ })).toHaveTextContent('Low Stock')
    expect(screen.getByRole('button', { name: /Cola/ })).toHaveTextContent(/out of stock/i)
    expect(screen.getByRole('button', { name: /Bread/ })).toHaveTextContent('40')
    expect(api.calls.find((c) => c.url.startsWith('/api/v1/pos/products?'))?.url).toContain('mainScreen=true')
  })

  it('labels the first chip "Main screen" when a display order is configured and filters by category', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    const chips = screen.getByRole('group', { name: 'Category' })
    expect(within(chips).getByRole('button', { name: 'Main screen' })).toHaveAttribute('aria-pressed', 'true')

    fireEvent.click(within(chips).getByRole('button', { name: /Bakery/ }))

    await waitFor(() => expect(screen.queryByRole('button', { name: /Milk 1L/ })).not.toBeInTheDocument())
    expect(screen.getByRole('button', { name: /Bread/ })).toBeInTheDocument()
    expect(api.calls.some((c) => c.url.includes('categoryId=2') && !c.url.includes('mainScreen'))).toBe(true)
  })

  it('says "All products" when no display order is configured', async () => {
    open()
    api.routes['GET /api/v1/pos/products'] = () => json(page(PRODUCTS, { isMainScreen: false }))
    await screen.findByRole('button', { name: /Milk 1L/ })

    expect(await screen.findByRole('button', { name: 'All Products' })).toBeInTheDocument()
  })

  it('hands a pressed tile to the cart as one piece, even when it is out of stock', async () => {
    open()

    fireEvent.click(await screen.findByRole('button', { name: /Cola/ }))

    expect(added).toEqual([{ product: expect.objectContaining({ id: 3 }), quantity: 1, source: 'tile' }])
    expect(await screen.findByRole('region', { name: 'Shopping Cart' })).toHaveTextContent('Cola')
  })

  it('shows an empty and an error state instead of a blank grid', async () => {
    open()
    api.routes['GET /api/v1/pos/products'] = () => json(page([]))
    expect(await screen.findByText('No products to show.')).toBeInTheDocument()
  })
})

describe('POS scanning', () => {
  it('looks a scanned code up as an exact barcode and adds it', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    scan('6001')

    await waitFor(() => expect(added).toHaveLength(1))
    expect(added[0]).toMatchObject({ product: { id: 1 }, quantity: 1, source: 'scan' })
    expect(api.calls.some((c) => c.url.includes('barcodeOnly=true') && c.url.includes('q=6001'))).toBe(true)
    expect(scanBox()).toHaveValue('')
  })

  it('"3*" remembers a quantity for the next scan only', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    scan('3*')
    expect(await screen.findByText('×3 on next scan')).toBeInTheDocument()
    expect(added).toHaveLength(0)

    scan('6001')
    await waitFor(() => expect(added).toHaveLength(1))
    expect(added[0]?.quantity).toBe(3)
    expect(screen.queryByText('×3 on next scan')).not.toBeInTheDocument()

    scan('6002')
    await waitFor(() => expect(added).toHaveLength(2))
    expect(added[1]?.quantity).toBe(1)
  })

  it('takes a quantity written in front of the code ("5x6001")', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    scan('5x6001')

    await waitFor(() => expect(added[0]?.quantity).toBe(5))
  })

  it('says so when the code is unknown, and keeps the box ready', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    scan('999999')

    expect(await screen.findByRole('alert')).toHaveTextContent('Not found')
    expect(added).toHaveLength(0)
  })

  it('refuses a QR code without asking the server', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })
    const before = api.calls.length

    scan('https://example.com/promo')

    expect(await screen.findByRole('alert')).toHaveTextContent(/link was scanned/i)
    expect(api.calls.length).toBe(before)
  })

  it('moves a typed digit to the scan box when nothing editable has focus', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })
    ;(document.activeElement as HTMLElement | null)?.blur()

    fireEvent.keyDown(document.body, { key: '6' })

    expect(scanBox()).toHaveFocus()
  })

  it('leaves typing in the search box alone', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })
    const search = screen.getByRole('combobox', { name: 'Search' })
    search.focus()

    fireEvent.keyDown(search, { key: '6' })

    expect(search).toHaveFocus()
  })
})

describe('POS search', () => {
  it('lists name matches after a pause and adds the one that is picked', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    fireEvent.change(screen.getByRole('combobox', { name: 'Search' }), { target: { value: 'wat' } })

    const option = await screen.findByRole('option', { name: /Water 500ml/ })
    expect(screen.queryByRole('option', { name: /Milk/ })).not.toBeInTheDocument()
    fireEvent.click(option)

    expect(added).toEqual([{ product: expect.objectContaining({ id: 2 }), quantity: 1, source: 'search' }])
    expect(screen.getByRole('combobox', { name: 'Search' })).toHaveValue('')
  })

  it('picks the highlighted match with the arrow keys and Enter', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })
    const box = screen.getByRole('combobox', { name: 'Search' })

    fireEvent.change(box, { target: { value: 'o' } }) // Milk? no: "Cola" and "Bakery"-less names containing "o"
    await screen.findAllByRole('option')
    fireEvent.keyDown(box, { key: 'ArrowDown' })
    fireEvent.keyDown(box, { key: 'Enter' })

    expect(added).toHaveLength(1)
    expect(added[0]?.source).toBe('search')
  })

  it('says when nothing matches', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    fireEvent.change(screen.getByRole('combobox', { name: 'Search' }), { target: { value: 'zzz' } })

    expect(await screen.findByText('No products to show.')).toBeInTheDocument()
  })
})

describe('POS quick actions and shortcuts', () => {
  it('opens the price check on F3 and shows the product that was scanned there', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    fireEvent.keyDown(document.body, { key: 'F3' })
    const dialog = await screen.findByRole('dialog')
    const box = within(dialog).getByRole('textbox')
    fireEvent.change(box, { target: { value: '6001' } })
    fireEvent.keyDown(box, { key: 'Enter' })

    expect(await within(dialog).findByText(/\$ 1\.75/)).toBeInTheDocument()
    expect(added).toHaveLength(0) // a price check never touches the cart
  })

  it('sends F-key commands to whoever handles them, and ignores them while a dialog is open', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })
    const hold = vi.fn()
    const off = posBus.onCommand('checkout', hold)

    fireEvent.keyDown(document.body, { key: 'Enter', ctrlKey: true })
    expect(hold).toHaveBeenCalledTimes(1)

    fireEvent.keyDown(document.body, { key: 'F3' })
    await screen.findByRole('dialog')
    fireEvent.keyDown(document.body, { key: 'Enter', ctrlKey: true })
    expect(hold).toHaveBeenCalledTimes(1)
    off()
  })

  it('runs the retrieve command from its button', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })
    const retrieve = vi.fn()
    const off = posBus.onCommand('retrieve', retrieve)

    fireEvent.click(screen.getByTestId('quick-retrieve'))

    expect(retrieve).toHaveBeenCalledTimes(1)
    off()
  })

  it('tells the cart slot nothing when there is no cart yet: commands report "unhandled"', () => {
    expect(posBus.run('checkout')).toBe(false)
  })
})

describe('POS chrome', () => {
  it('offers the way back to management and keeps the rail out of the page', async () => {
    open()
    await screen.findByRole('button', { name: /Milk 1L/ })

    expect(screen.getByRole('link', { name: /Management/ }).getAttribute('href')).toBeTruthy()
    expect(screen.queryByRole('navigation', { name: 'Navigation' })).not.toBeInTheDocument()
  })
})
