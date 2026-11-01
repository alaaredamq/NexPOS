import { expect, test, type Page } from '@playwright/test'

// Track P4 happy path: pay at the till, change for cash, a sale on account, last sales. Run against the .NET host serving the built SPA on the
// isolated demo DB:
//   BASE_URL=http://localhost:5058 npx playwright test e2e/pos-checkout.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.
// The licence of a fresh demo host may not allow selling; the host must run with a licence that does (see PosTestKit.Selling for the idea).

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('checkout: pay in cash with change, sell on account, reprint from last sales', async ({ browser }) => {
  const page = await (await browser.newContext()).newPage()
  await signIn(page, /Sara/, '1111', '/pos')
  const cart = page.getByRole('region', { name: 'Shopping Cart' })

  // 1. one product, cash, a note: the change shows before and after the sale
  await page.getByTestId('product-grid').getByRole('button').first().click()
  const checkout = cart.getByRole('button', { name: 'Checkout' })
  await expect(checkout).toBeEnabled()
  await checkout.click()

  const pay = page.getByRole('dialog', { name: 'Payment' })
  await expect(pay.getByText('Total due')).toBeVisible()
  await expect(pay.getByRole('radio', { checked: true })).toHaveCount(1)
  await pay.getByRole('radio').first().click()
  const due = (await pay.getByLabel('Total Amount (Editable)').inputValue()).replace(/,/g, '')
  const received = Math.ceil(Number(due) / 100) * 100 + 100
  await pay.getByLabel('Amount received').fill(String(received))
  await expect(pay.getByText('Change due')).toBeVisible()
  await pay.getByRole('button', { name: 'Complete' }).click()

  const done = page.getByRole('dialog', { name: 'Sale Complete!' })
  await expect(done).toBeVisible()
  await expect(done.getByText('Change due')).toBeVisible()
  await expect(cart.getByText('Cart is Empty')).toBeVisible()
  await done.getByRole('button', { name: 'New sale' }).click()
  await expect(page.getByRole('dialog')).toHaveCount(0)

  // 2. the same again, but on a customer's account
  await page.getByTestId('product-grid').getByRole('button').first().click()
  await expect(checkout).toBeEnabled()
  await checkout.click()
  const pay2 = page.getByRole('dialog', { name: 'Payment' })
  await pay2.getByRole('combobox', { name: 'Customer (Optional)' }).click()
  await page.getByRole('option').first().click()
  await pay2.getByRole('button', { name: 'Add to Debt' }).click()
  await expect(page.getByRole('dialog', { name: 'Sale Complete!' })).toBeVisible()
  await page.getByRole('button', { name: 'New sale' }).click()

  // 3. last sales: the cashier's latest sales are listed and can be reprinted (the demo host has a no-op printer)
  await page.keyboard.press('F7')
  const recent = page.getByRole('dialog', { name: 'Last sales' })
  await expect(recent.getByRole('listitem').first()).toBeVisible() // the demo data already holds older sales: newest first
  await recent.getByRole('button', { name: 'Print receipt' }).first().click()
  await recent.getByRole('button', { name: 'Close' }).last().click()
  await expect(page.getByRole('dialog')).toHaveCount(0)
})
