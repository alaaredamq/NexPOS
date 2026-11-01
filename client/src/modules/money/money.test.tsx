import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'
import { dayOf, endOfDay, startOfDay } from './taxDates'

const page = <T,>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 25 })

const problemResponse = (status: number, body: Record<string, unknown>) =>
  new Response(JSON.stringify({ status, title: 'Problem', ...body }), { status, headers: { 'Content-Type': 'application/problem+json' } })

const currency = (id: number, code: string, overrides = {}) => ({
  id,
  code,
  name: `${code} money`,
  notation: code === 'USD' ? '$' : code,
  rateToUsd: id === 1 ? 1 : 89500,
  isDefault: id === 1,
  isActive: true,
  createdAt: '2026-01-05T10:00:00Z',
  updatedAt: null,
  ...overrides,
})

const rate = (id: number, overrides = {}) => ({
  id,
  fromCurrencyId: 1,
  fromCurrencyCode: 'USD',
  toCurrencyId: 3,
  toCurrencyCode: 'LBP',
  rate: 90000,
  supplierId: null,
  supplierName: null,
  createdAt: '2026-01-05T10:00:00Z',
  updatedAt: null,
  ...overrides,
})

const tax = (id: number, overrides = {}) => ({
  id,
  name: `Tax ${id}`,
  description: null,
  rate: 11,
  type: 0,
  taxCode: null,
  isDefault: false,
  isActive: true,
  isInclusive: false,
  applyToAllProducts: true,
  categoryId: null,
  categoryName: null,
  effectiveFrom: null,
  effectiveTo: null,
  createdAt: '2026-01-05T10:00:00Z',
  ...overrides,
})

const method = (id: number, name: string, overrides = {}) => ({
  id,
  name,
  isDefault: false,
  isSystem: false,
  givesChange: name.toLowerCase().includes('cash'),
  createdAt: '2026-01-05T10:00:00Z',
  ...overrides,
})

let api: FakeApi

beforeAll(async () => {
  await loadNamespaces('common', 'money')
  // The pages are lazy chunks; load them up front so the first test does not spend its findBy timeout on the transform.
  await Promise.all([import('./CurrenciesPage'), import('./ExchangeRatesPage'), import('./TaxesPage'), import('./DiscountsPage'), import('./PaymentMethodsPage'), import('./CodeListsPage')])
})

beforeEach(async () => {
  stubMatchMedia()
  window.innerWidth = 1024
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

const CURRENCY_ADMIN = [Permissions.Currency_View, Permissions.Currency_Create, Permissions.Currency_Edit, Permissions.Currency_Delete]
const TAX_ADMIN = [Permissions.Tax_View, Permissions.Tax_Create, Permissions.Tax_Edit, Permissions.Tax_Delete, Permissions.Tax_Manage]

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/currencies'] = () => json(page([currency(1, 'USD'), currency(2, 'EUR', { rateToUsd: 1.1 }), currency(3, 'LBP', { isActive: false })]))
  api.routes['GET /api/v1/exchange-rates'] = () => json(page([rate(1), rate(2, { fromCurrencyId: 2, fromCurrencyCode: 'EUR', supplierId: 4, supplierName: 'Acme Foods' })]))
  api.routes['GET /api/v1/exchange-rates/suppliers'] = () => json([{ id: 4, name: 'Acme Foods' }])
  api.routes['GET /api/v1/taxes'] = () =>
    json(page([tax(1, { name: 'VAT 11', isDefault: true, effectiveFrom: '2026-01-01T00:00:00Z', effectiveTo: '2026-12-31T23:59:59Z' }), tax(2, { name: 'Old levy', isActive: false, type: 3, categoryName: 'Drinks', categoryId: 7 })]))
  api.routes['GET /api/v1/taxes/categories'] = () => json([{ id: 7, name: 'Drinks' }])
  api.routes['GET /api/v1/discounts'] = () => json(page([{ id: 1, name: 'No Discount', rate: 0, isDefault: true, createdAt: '2026-01-05T10:00:00Z' }]))
  api.routes['GET /api/v1/payment-methods'] = () =>
    json(page([method(1, 'Cash', { isDefault: true }), method(2, 'Card'), method(3, 'Customer Debt', { isSystem: true })]))
  api.routes['GET /api/v1/code-lists'] = () =>
    json(page([{ id: 5, key: 'units', name: 'Units', description: null, isActive: true, allowMultipleSelection: false, displayOrder: 1, valueCount: 1, createdAt: '2026-01-05T10:00:00Z' }]))
  return mountModule(path, routes)
}

describe('taxDates', () => {
  it('a period starts at the first second of its first day and ends at the last second of its last day, both in UTC', () => {
    expect(startOfDay('2026-03-01')).toBe('2026-03-01T00:00:00Z')
    expect(endOfDay('2026-12-31')).toBe('2026-12-31T23:59:59Z')
    expect(startOfDay('')).toBeNull()
    expect(endOfDay('')).toBeNull()
  })

  it('reads the day back the way it was written', () => {
    expect(dayOf('2026-12-31T23:59:59Z')).toBe('2026-12-31')
    expect(dayOf('2026-03-01T00:00:00Z')).toBe('2026-03-01')
    expect(dayOf(null)).toBe('')
  })
})

describe('currencies', () => {
  it('lists the currencies with their rate, the default badge and the tiles', async () => {
    open('/currency', ...CURRENCY_ADMIN)

    expect(await screen.findByText('EUR money')).toBeInTheDocument()
    expect(screen.getByText('89,500')).toBeInTheDocument()
    expect(screen.getByText('1.1')).toBeInTheDocument()
    expect(screen.getAllByText('Default').length).toBeGreaterThan(0)
    expect(screen.getByText('Inactive')).toBeInTheDocument()
    expect(await screen.findByText('Active Currencies')).toBeInTheDocument()
  })

  it('offers no delete on the default currency and no "make default" on an inactive one', async () => {
    open('/currency', ...CURRENCY_ADMIN)
    await screen.findByText('EUR money')

    const usd = screen.getByText('USD money', { selector: 'td' }).closest('tr')!
    expect(within(usd).queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    expect(within(usd).queryByRole('button', { name: 'Make default' })).not.toBeInTheDocument()
    const eur = screen.getByText('EUR money').closest('tr')!
    expect(within(eur).getByRole('button', { name: 'Make default' })).toBeInTheDocument()
    expect(within(eur).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
    const lbp = screen.getByText('LBP money').closest('tr')!
    expect(within(lbp).queryByRole('button', { name: 'Make default' })).not.toBeInTheDocument()
  })

  it('making a currency the default calls the default endpoint and says so', async () => {
    open('/currency', ...CURRENCY_ADMIN)
    api.routes['PUT /api/v1/currencies/2/default'] = () => json(currency(2, 'EUR', { isDefault: true }))
    await screen.findByText('EUR money')

    fireEvent.click(within(screen.getByText('EUR money').closest('tr')!).getByRole('button', { name: 'Make default' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'PUT' && c.url.endsWith('/currencies/2/default'))).toBe(true))
    expect(await screen.findByText('EUR is now the default currency.')).toBeInTheDocument()
  })

  it('refuses an empty code and a zero rate without calling the API', async () => {
    open('/currency', ...CURRENCY_ADMIN)
    await screen.findByText('EUR money')

    fireEvent.click(screen.getByRole('button', { name: 'Add Currency' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Currency' })
    fireEvent.change(within(dialog).getByLabelText(/Units per US dollar/), { target: { value: '0' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Currency' }))

    expect(await within(dialog).findByText('Enter a currency code.')).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a rate greater than zero.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('saves trimmed text with the rate, and puts a taken code on its field', async () => {
    open('/currency', ...CURRENCY_ADMIN)
    api.routes['POST /api/v1/currencies'] = () => problemResponse(400, { code: 'validation_failed', errors: { code: ['A currency with that code already exists.'] } })
    await screen.findByText('EUR money')

    fireEvent.click(screen.getByRole('button', { name: 'Add Currency' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Currency' })
    fireEvent.change(within(dialog).getByLabelText(/^Code/), { target: { value: ' gbp ' } })
    fireEvent.change(within(dialog).getByLabelText(/^Symbol/), { target: { value: '£' } })
    fireEvent.change(within(dialog).getByLabelText(/^Name/), { target: { value: ' Pound ' } })
    fireEvent.change(within(dialog).getByLabelText(/Units per US dollar/), { target: { value: '0.79' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Currency' }))

    expect(await within(dialog).findByText('A currency with that code already exists.')).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ code: 'gbp', name: 'Pound', notation: '£', rateToUsd: 0.79, isDefault: false, isActive: true })
    expect(screen.getByRole('dialog', { name: 'Add Currency' })).toBeInTheDocument()
  })

  it('editing the default currency keeps its default switch on and disabled', async () => {
    open('/currency', ...CURRENCY_ADMIN)
    await screen.findByText('EUR money')

    fireEvent.click(within(screen.getByText('USD money', { selector: 'td' }).closest('tr')!).getByRole('button', { name: 'Edit' }))
    const dialog = await screen.findByRole('dialog', { name: 'Edit Currency' })

    const toggle = within(dialog).getByRole('switch', { name: 'Default currency' })
    expect(toggle).toBeDisabled()
    expect(toggle).toHaveAttribute('aria-checked', 'true')
  })

  it('deleting asks first and shows the reason inside the dialog when the currency is in use', async () => {
    open('/currency', ...CURRENCY_ADMIN)
    api.routes['DELETE /api/v1/currencies/2'] = () => problemResponse(409, { code: 'currency_in_use', detail: 'Suppliers still use this currency.' })
    await screen.findByText('EUR money')

    fireEvent.click(within(screen.getByText('EUR money').closest('tr')!).getByRole('button', { name: 'Delete' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Delete' }))

    expect(await within(dialog).findByText('Suppliers still use this currency.')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('a view-only user sees the list and the converter but no buttons that change anything', async () => {
    open('/currency', Permissions.Currency_View)
    await screen.findByText('EUR money')

    for (const name of ['Edit', 'Delete', 'Make default', 'Add Currency']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
    }
    expect(screen.getByRole('button', { name: 'Convert' })).toBeInTheDocument()
  })

  it('the converter asks the server and shows the converted amount', async () => {
    open('/currency', ...CURRENCY_ADMIN)
    api.routes['POST /api/v1/currencies/convert'] = () => json({ amount: 2, converted: 179000, fromCode: 'USD', toCode: 'LBP' })
    await screen.findByText('EUR money')

    const form = await screen.findByRole('form', { name: 'Converter' })
    await waitFor(() => expect(within(form).getAllByRole('combobox')[0]).toHaveValue('USD - USD money'))
    fireEvent.change(within(form).getByLabelText(/^Amount/), { target: { value: '2' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Convert' }))

    expect(await within(form).findByRole('status')).toHaveTextContent('2.00 USD = 179,000.00 LBP')
    expect(api.calls.find((c) => c.url.endsWith('/currencies/convert'))?.body).toEqual({ amount: 2, fromCurrencyId: 1, toCurrencyId: 2 })
  })

  it('without Currency.View the route bounces to the forbidden page', async () => {
    const router = open('/currency', Permissions.Orders_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('the tab bar', () => {
  it('shows only the tabs the user may open', async () => {
    open('/currency', Permissions.Currency_View, Permissions.Discounts_View)
    await screen.findByText('EUR money')

    const tabs = within(screen.getByRole('navigation', { name: 'Money configuration' }))
    expect(tabs.getByRole('link', { name: 'Currencies' })).toBeInTheDocument()
    expect(tabs.getByRole('link', { name: 'Exchange Rates' })).toBeInTheDocument()
    expect(tabs.getByRole('link', { name: 'Discounts' })).toBeInTheDocument()
    expect(tabs.queryByRole('link', { name: 'Taxes' })).not.toBeInTheDocument()
    expect(tabs.queryByRole('link', { name: 'Payment Methods' })).not.toBeInTheDocument()
    expect(tabs.queryByRole('link', { name: 'Code Lists' })).not.toBeInTheDocument()
  })

  it('code lists open for Settings.Manage alone, as the Admin role holds Manage but not View', async () => {
    open('/code-lists', Permissions.Settings_Manage)

    expect(await screen.findByText('Units')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add Code List' })).toBeInTheDocument()
  })
})

describe('exchange rates', () => {
  it('lists the pairs and marks the store-wide ones', async () => {
    open('/currency/exchange-rates', ...CURRENCY_ADMIN)

    expect(await screen.findByText('Acme Foods')).toBeInTheDocument()
    expect(screen.getAllByText('90,000')).toHaveLength(2)
    expect(screen.getByText('Store-wide')).toBeInTheDocument()
  })

  it('refuses a pair of the same currency, then posts the pair with the supplier left empty', async () => {
    open('/currency/exchange-rates', ...CURRENCY_ADMIN)
    api.routes['POST /api/v1/exchange-rates'] = () => json(rate(9), 201)
    await screen.findByText('Acme Foods')

    fireEvent.click(screen.getByRole('button', { name: 'Add Exchange Rate' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Exchange Rate' })
    const pick = async (index: number, name: string) => {
      fireEvent.click(within(dialog).getAllByRole('combobox')[index]!)
      fireEvent.click(await screen.findByRole('option', { name }))
    }
    await pick(0, 'USD - USD money')
    await pick(1, 'USD - USD money')
    fireEvent.change(within(dialog).getByLabelText(/^Rate/), { target: { value: '1.25' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Exchange Rate' }))
    expect(await within(dialog).findByText('Choose two different currencies.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)

    await pick(1, 'EUR - EUR money')
    expect(await within(dialog).findByText(/1 USD = 1.25 EUR/)).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Exchange Rate' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ fromCurrencyId: 1, toCurrencyId: 2, rate: 1.25, supplierId: null }))
  })
})

describe('taxes', () => {
  it('lists the rates as percentages with the type, the default badge and the whole days of the period', async () => {
    window.innerWidth = 1400 // the category column hides below 1200 px
    open('/tax', ...TAX_ADMIN)

    expect(await screen.findByText('VAT 11')).toBeInTheDocument()
    expect(screen.getAllByText(/11%/).length).toBe(2)
    expect(screen.getByText('Excise tax')).toBeInTheDocument()
    expect(screen.getByText('Drinks')).toBeInTheDocument()
    // The period is shown by its UTC days, so "until 31 Dec" reads 31 Dec in every time zone.
    expect(screen.getByText(/Dec 31, 2026/)).toBeInTheDocument()
    expect(screen.getByText(/Jan 1, 2026/)).toBeInTheDocument()
  })

  it('saves a period as start of the first day to end of the last day, and nothing as null', async () => {
    open('/tax', ...TAX_ADMIN)
    api.routes['POST /api/v1/taxes'] = () => json(tax(9), 201)
    await screen.findByText('VAT 11')

    fireEvent.click(screen.getByRole('button', { name: 'Add Tax' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Tax' })
    fireEvent.change(within(dialog).getByLabelText(/^Tax name/), { target: { value: ' Service ' } })
    fireEvent.change(within(dialog).getByLabelText(/^Rate/), { target: { value: '8.5' } })
    fireEvent.change(within(dialog).getByLabelText(/^Effective from/), { target: { value: '2026-03-01' } })
    fireEvent.change(within(dialog).getByLabelText(/^Effective to/), { target: { value: '2026-12-31' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Tax' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toMatchObject({
      name: 'Service',
      rate: 8.5,
      type: 0,
      taxCode: null,
      description: null,
      effectiveFrom: '2026-03-01T00:00:00Z',
      effectiveTo: '2026-12-31T23:59:59Z',
      categoryId: null,
      isDefault: false,
      isActive: true,
    }))
  })

  it('refuses a rate above 100 and an end before the start', async () => {
    open('/tax', ...TAX_ADMIN)
    await screen.findByText('VAT 11')

    fireEvent.click(screen.getByRole('button', { name: 'Add Tax' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Tax' })
    fireEvent.change(within(dialog).getByLabelText(/^Tax name/), { target: { value: 'Bad' } })
    fireEvent.change(within(dialog).getByLabelText(/^Rate/), { target: { value: '101' } })
    fireEvent.change(within(dialog).getByLabelText(/^Effective from/), { target: { value: '2026-06-01' } })
    fireEvent.change(within(dialog).getByLabelText(/^Effective to/), { target: { value: '2026-01-01' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Tax' }))

    expect(await within(dialog).findByText("The end date can't be before the start date.")).toBeInTheDocument()
    expect(within(dialog).getByText('Enter a percentage from 0 to 100.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('"make default" and "deactivate" ask first and call their own endpoints', async () => {
    open('/tax', ...TAX_ADMIN)
    api.routes['POST /api/v1/taxes/2/default'] = () => json(tax(2, { isDefault: true }))
    api.routes['POST /api/v1/taxes/1/deactivate'] = () => json(tax(1, { isActive: false }))
    await screen.findByText('VAT 11')

    // VAT 11 is the default already, so only the other tax offers it; the inactive tax cannot be deactivated again.
    const vat = screen.getByText('VAT 11').closest('tr')!
    const levy = screen.getByText('Old levy').closest('tr')!
    expect(within(vat).queryByRole('button', { name: 'Make default' })).not.toBeInTheDocument()
    expect(within(levy).queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument()

    fireEvent.click(within(levy).getByRole('button', { name: 'Make default' }))
    let dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Make default' }))
    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url.endsWith('/taxes/2/default'))).toBe(true))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())

    fireEvent.click(within(vat).getByRole('button', { name: 'Deactivate' }))
    dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Deactivate' }))
    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url.endsWith('/taxes/1/deactivate'))).toBe(true))
  })

  it('a user with Edit only can edit but not make default, deactivate, delete or add', async () => {
    open('/tax', Permissions.Tax_View, Permissions.Tax_Edit)
    await screen.findByText('VAT 11')

    expect(screen.getAllByRole('button', { name: 'Edit' })).toHaveLength(2)
    for (const name of ['Make default', 'Deactivate', 'Delete', 'Add Tax']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
    }
  })

  it('the calculator posts the amount and the tax picked, and shows base, tax and total', async () => {
    open('/tax', ...TAX_ADMIN)
    api.routes['POST /api/v1/taxes/calculate'] = () => json({ baseAmount: 200, taxAmount: 22, totalAmount: 222 })
    await screen.findByText('VAT 11')

    const form = await screen.findByRole('form', { name: 'Tax calculator' })
    fireEvent.change(within(form).getByLabelText(/^Base amount/), { target: { value: '200' } })
    fireEvent.click(within(form).getByRole('button', { name: 'Calculate' }))

    const result = await within(form).findByRole('status')
    expect(result).toHaveTextContent('200.00')
    expect(result).toHaveTextContent('22.00')
    expect(result).toHaveTextContent('222.00')
    expect(api.calls.find((c) => c.url.endsWith('/taxes/calculate'))?.body).toEqual({ baseAmount: 200, taxId: null })
  })
})

describe('discounts', () => {
  it('lists the discounts and refuses a percentage above 100', async () => {
    open('/discounts', Permissions.Discounts_View, Permissions.Discounts_Manage)
    expect(await screen.findByText('No Discount')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Add Discount' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Discount' })
    fireEvent.change(within(dialog).getByLabelText(/^Name/), { target: { value: 'Staff' } })
    fireEvent.change(within(dialog).getByLabelText(/^Discount \(%\)/), { target: { value: '120' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Discount' }))

    expect(await within(dialog).findByText('Enter a percentage from 0 to 100.')).toBeInTheDocument()
    expect(api.calls.some((c) => c.method === 'POST')).toBe(false)
  })

  it('creates a discount with its percentage and default flag', async () => {
    open('/discounts', Permissions.Discounts_View, Permissions.Discounts_Manage)
    api.routes['POST /api/v1/discounts'] = () => json({ id: 2, name: 'Staff', rate: 15, isDefault: true, createdAt: '2026-01-05T10:00:00Z' }, 201)
    await screen.findByText('No Discount')

    fireEvent.click(screen.getByRole('button', { name: 'Add Discount' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Discount' })
    fireEvent.change(within(dialog).getByLabelText(/^Name/), { target: { value: ' Staff ' } })
    fireEvent.change(within(dialog).getByLabelText(/^Discount \(%\)/), { target: { value: '15' } })
    fireEvent.click(within(dialog).getByRole('switch', { name: 'Default discount' }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Discount' }))

    await waitFor(() => expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Staff', rate: 15, isDefault: true }))
  })

  it('a view-only user cannot add, edit or delete', async () => {
    open('/discounts', Permissions.Discounts_View)
    await screen.findByText('No Discount')

    for (const name of ['Add Discount', 'Edit', 'Delete']) {
      expect(screen.queryByRole('button', { name })).not.toBeInTheDocument()
    }
  })
})

describe('payment methods', () => {
  it('lists the methods, marks the system one and gives it no edit or delete', async () => {
    open('/payment-methods', Permissions.PaymentMethods_View, Permissions.PaymentMethods_Manage)

    expect(await screen.findByText('Customer Debt')).toBeInTheDocument()
    expect(screen.getByText('System')).toBeInTheDocument()
    const debt = screen.getByText('Customer Debt').closest('tr')!
    expect(within(debt).queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(within(debt).queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    const card = screen.getByText('Card').closest('tr')!
    expect(within(card).getByRole('button', { name: 'Edit' })).toBeInTheDocument()
    expect(within(card).getByRole('button', { name: 'Delete' })).toBeInTheDocument()
  })

  it('puts a taken name on the field and keeps the dialog open', async () => {
    open('/payment-methods', Permissions.PaymentMethods_View, Permissions.PaymentMethods_Manage)
    api.routes['POST /api/v1/payment-methods'] = () => problemResponse(400, { code: 'validation_failed', errors: { name: ['A payment method with that name already exists.'] } })
    await screen.findByText('Customer Debt')

    fireEvent.click(screen.getByRole('button', { name: 'Add Payment Method' }))
    const dialog = await screen.findByRole('dialog', { name: 'Add Payment Method' })
    fireEvent.change(within(dialog).getByLabelText(/^Name/), { target: { value: ' Card ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Payment Method' }))

    expect(await within(dialog).findByText('A payment method with that name already exists.')).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ name: 'Card', isDefault: false })
  })
})

describe('code lists', () => {
  const details = (values: unknown[]) => ({
    list: { id: 5, key: 'units', name: 'Units', description: null, isActive: true, allowMultipleSelection: false, displayOrder: 1, valueCount: values.length, createdAt: '2026-01-05T10:00:00Z' },
    values,
  })
  const value = (id: number, text: string) => ({ id, codeListId: 5, value: text, displayText: null, description: null, isActive: true, displayOrder: id, metadata: null })

  it('opens the values of a list, adds one and lands a duplicate on the value field', async () => {
    open('/code-lists', Permissions.Settings_View, Permissions.Settings_Manage)
    api.routes['GET /api/v1/code-lists/5'] = () => json(details([value(1, 'kg')]))
    api.routes['POST /api/v1/code-lists/5/values'] = () => problemResponse(400, { code: 'validation_failed', errors: { value: ['This list already has that value.'] } })
    await screen.findByText('units')

    fireEvent.click(screen.getByRole('button', { name: 'Manage values' }))
    const dialog = await screen.findByRole('dialog', { name: 'Values of Units' })
    expect(await within(dialog).findByText('kg')).toBeInTheDocument()

    fireEvent.change(within(dialog).getByLabelText(/^Value/), { target: { value: ' KG ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add Value' }))

    expect(await within(dialog).findByText('This list already has that value.')).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === 'POST')?.body).toMatchObject({ value: 'KG', displayText: null, isActive: true })
  })

  it('a viewer can read the values but has no form to change them', async () => {
    open('/code-lists', Permissions.Settings_View)
    api.routes['GET /api/v1/code-lists/5'] = () => json(details([value(1, 'kg')]))
    await screen.findByText('units')

    fireEvent.click(screen.getByRole('button', { name: 'Manage values' }))
    const dialog = await screen.findByRole('dialog', { name: 'Values of Units' })

    expect(await within(dialog).findByText('kg')).toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Add Value' })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add Code List' })).not.toBeInTheDocument()
  })
})
