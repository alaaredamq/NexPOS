import { expect, test, type Page } from '@playwright/test'

// Track A3 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5058 npx playwright test e2e/money.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('money: add a currency and see the converter use its rate, a pair rate, a tax, a discount and a payment method - the system method is protected and a cashier is turned away', async ({ browser }) => {
  const admin = await (await browser.newContext()).newPage()
  await signIn(admin, /Layla/, '1234', '/currency')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Currency Management' })).toBeVisible()

  // A currency worth 2 per dollar, then the converter shows what the till would show.
  const stamp = Date.now().toString().slice(-6)
  const code = `T${stamp}`
  await admin.getByRole('button', { name: 'Add Currency' }).click()
  let dialog = admin.getByRole('dialog', { name: 'Add Currency' })
  await dialog.getByLabel(/^Code/).fill(code)
  await dialog.getByLabel(/^Symbol/).fill('¤')
  await dialog.getByLabel(/^Name/).fill(`E2E ${stamp}`)
  await dialog.getByLabel(/Units per US dollar/).fill('2')
  await dialog.getByRole('button', { name: 'Add Currency' }).click()
  await expect(admin.getByText('Currency created.')).toBeVisible()
  await expect(admin.getByRole('row', { name: new RegExp(code) })).toBeVisible()

  // The same code again is refused on the field.
  await admin.getByRole('button', { name: 'Add Currency' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add Currency' })
  await dialog.getByLabel(/^Code/).fill(code.toLowerCase())
  await dialog.getByLabel(/^Symbol/).fill('¤')
  await dialog.getByLabel(/^Name/).fill('Again')
  await dialog.getByLabel(/Units per US dollar/).fill('3')
  await dialog.getByRole('button', { name: 'Add Currency' }).click()
  await expect(dialog.getByText('A currency with that code already exists.')).toBeVisible()
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  const converter = admin.getByRole('form', { name: 'Converter' })
  await converter.getByRole('combobox').nth(1).click()
  await admin.getByRole('option', { name: new RegExp(`^${code}`) }).click()
  await converter.getByLabel(/^Amount/).fill('10')
  await converter.getByRole('button', { name: 'Convert' }).click()
  await expect(converter.getByRole('status')).toContainText(`10.00 USD = 20.00 ${code}`)

  // A pair rate between USD and the new currency.
  await admin.goto('/app/currency/exchange-rates')
  await admin.getByRole('button', { name: 'Add Exchange Rate' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add Exchange Rate' })
  await dialog.getByRole('combobox').nth(0).click()
  await admin.getByRole('option', { name: /^USD/ }).click()
  await dialog.getByRole('combobox').nth(1).click()
  await admin.getByRole('option', { name: new RegExp(`^${code}`) }).click()
  await dialog.getByLabel(/^Rate/).fill('2.5')
  await dialog.getByRole('button', { name: 'Add Exchange Rate' }).click()
  await expect(admin.getByText('Exchange rate created.')).toBeVisible()

  // A tax that ends at the end of its last day, then the calculator.
  await admin.goto('/app/tax')
  await admin.getByRole('button', { name: 'Add Tax' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add Tax' })
  const taxName = `E2E tax ${stamp}`
  await dialog.getByLabel(/^Tax name/).fill(taxName)
  await dialog.getByLabel(/^Rate/).fill('10')
  await dialog.getByRole('button', { name: 'Add Tax' }).click()
  await expect(admin.getByText('Tax created.')).toBeVisible()
  const calculator = admin.getByRole('form', { name: 'Tax calculator' })
  await calculator.getByLabel(/^Base amount/).fill('200')
  await calculator.getByRole('combobox').click()
  await admin.getByRole('option', { name: new RegExp(taxName) }).click()
  await calculator.getByRole('button', { name: 'Calculate' }).click()
  await expect(calculator.getByRole('status')).toContainText('20.00')
  await expect(calculator.getByRole('status')).toContainText('220.00')

  // A discount.
  await admin.goto('/app/discounts')
  await admin.getByRole('button', { name: 'Add Discount' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add Discount' })
  const discountName = `E2E discount ${stamp}`
  await dialog.getByLabel(/^Name/).fill(discountName)
  await dialog.getByLabel(/^Discount \(%\)/).fill('7.5')
  await dialog.getByRole('button', { name: 'Add Discount' }).click()
  await expect(admin.getByText('Discount created.')).toBeVisible()
  await expect(admin.getByRole('row', { name: new RegExp(discountName) })).toContainText('7.5%')

  // Payment methods: "Customer Debt" is listed but cannot be edited or deleted.
  await admin.goto('/app/payment-methods')
  const debt = admin.getByRole('row', { name: /Customer Debt/ })
  await expect(debt).toBeVisible()
  await expect(debt.getByText('System')).toBeVisible()
  await expect(debt.getByRole('button', { name: 'Edit' })).toHaveCount(0)
  await expect(debt.getByRole('button', { name: 'Delete' })).toHaveCount(0)
  await admin.getByRole('button', { name: 'Add Payment Method' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add Payment Method' })
  await dialog.getByLabel(/^Name/).fill('customer debt')
  await dialog.getByRole('button', { name: 'Add Payment Method' }).click()
  await expect(dialog.getByText(/reserved for sales put on a customer/)).toBeVisible()
  await dialog.getByLabel(/^Name/).fill(`E2E wire ${stamp}`)
  await dialog.getByRole('button', { name: 'Add Payment Method' }).click()
  await expect(admin.getByText('Payment method created.')).toBeVisible()

  // The till reads the same lists straight away.
  const till = await admin.request.get('/api/v1/pos/discounts')
  expect((await till.json()).some((d: { name: string }) => d.name === discountName)).toBe(true)

  // A cashier holds none of these permissions.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/currency')
  await expect(cashier).toHaveURL(/\/app\/forbidden/)
})
