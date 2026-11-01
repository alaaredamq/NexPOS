import { expect, test, type Page } from '@playwright/test'

// Track S3 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5058 npx playwright test e2e/customers.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('customers: add, find, edit, run a debt up and down through zero, export - and a cashier cannot edit or touch debt', async ({ browser }) => {
  const admin = await (await browser.newContext({ acceptDownloads: true })).newPage()
  await signIn(admin, /Layla/, '1234', '/customers')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Customers', exact: true })).toBeVisible()

  const first = `E2E${Date.now()}`
  await admin.getByRole('button', { name: 'Add Customer' }).click()
  await admin.getByLabel(/First Name/).fill(first)
  await admin.getByLabel(/Last Name/).fill('Tester')
  await admin.getByLabel(/Phone Number/).fill('71000111')
  await admin.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText('Customer created successfully!')).toBeVisible()

  await admin.getByRole('searchbox').or(admin.getByRole('textbox', { name: /Search/ })).first().fill(first)
  const row = admin.getByRole('row', { name: new RegExp(first) })
  await expect(row).toBeVisible()

  await row.getByRole('button', { name: 'Edit' }).click()
  await admin.getByLabel(/Email Address/).fill(`${first.toLowerCase()}@example.com`)
  await admin.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByRole('dialog')).toHaveCount(0)
  await expect(row.getByText(`${first.toLowerCase()}@example.com`)).toBeVisible()

  await admin.getByRole('link', { name: `${first} Tester` }).click()
  await expect(admin).toHaveURL(/\/app\/customers\/\d+$/)
  await expect(main.getByRole('heading', { name: `${first} Tester` })).toBeVisible()
  await expect(main.getByText('Balanced - No debt')).toBeVisible()

  // The customer owes 25 ...
  await main.getByRole('button', { name: 'Add Transaction' }).click()
  let dialog = admin.getByRole('dialog', { name: 'Add Debt Transaction' })
  await dialog.getByLabel(/Amount/).fill('25')
  await dialog.getByLabel(/Note/).fill('Credit sale')
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText('Debt updated successfully!')).toBeVisible()
  await expect(main.getByText(/Customer owes me:.*25\.00/)).toBeVisible()
  await expect(main.getByText('Credit sale')).toBeVisible()

  // ... pays 30: more than owed, the dialog warns and the balance goes negative (the business owes 5).
  await main.getByRole('button', { name: 'Add Transaction' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add Debt Transaction' })
  await dialog.getByRole('radio', { name: /I got paid/ }).check({ force: true })
  await dialog.getByLabel(/Amount/).fill('30')
  await expect(dialog.getByText(/so you will owe the customer/)).toBeVisible()
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(main.getByText(/I owe customer:.*5\.00/)).toBeVisible()

  // Back to zero.
  await main.getByRole('button', { name: 'Add Transaction' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add Debt Transaction' })
  await dialog.getByLabel(/Amount/).fill('5')
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(main.getByText('Balanced - No debt')).toBeVisible()
  await expect(main.getByRole('table', { name: 'Debt ledger' }).getByRole('row')).toHaveCount(4) // header + three ledger entries

  await admin.goto('/app/customers')
  const download = admin.waitForEvent('download')
  await admin.getByRole('button', { name: 'Export CSV' }).click()
  expect((await download).suggestedFilename()).toMatch(/^customers_export_.*\.csv$/)

  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/customers')
  await expect(cashier.getByRole('button', { name: 'Add Customer' }).first()).toBeVisible()
  await expect(cashier.getByRole('button', { name: 'Edit' })).toHaveCount(0)
  await expect(cashier.getByRole('button', { name: 'Add Debt Transaction' })).toHaveCount(0)
  await cashier.getByRole('link', { name: `${first} Tester` }).or(cashier.getByRole('row').nth(1).getByRole('link')).first().click()
  await expect(cashier.getByRole('button', { name: 'Add Transaction' })).toHaveCount(0)
})
