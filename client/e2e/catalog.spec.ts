import { expect, test, type Page } from '@playwright/test'

// Track C2 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5057 npx playwright test e2e/catalog.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('categories: create, edit, open details, export, and a cashier cannot change anything', async ({ browser }) => {
  const admin = await (await browser.newContext({ acceptDownloads: true })).newPage()
  await signIn(admin, /Layla/, '1234', '/categories')
  await expect(admin.getByRole('main').getByRole('heading', { name: 'Categories', exact: true })).toBeVisible()

  const name = `E2E ${Date.now()}`
  await admin.getByRole('button', { name: 'Add Category' }).click()
  await admin.getByLabel(/Category name/).fill(name)
  await admin.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText('Category created successfully!')).toBeVisible()

  await admin.getByRole('searchbox').or(admin.getByRole('textbox', { name: /Search/ })).first().fill(name)
  const row = admin.getByRole('row', { name: new RegExp(name) })
  await expect(row).toBeVisible()
  await row.getByRole('button', { name: 'Edit' }).click()
  await admin.getByLabel(/Category name/).fill(`${name} v2`)
  await admin.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByRole('dialog')).toHaveCount(0)
  await expect(admin.getByRole('link', { name: `${name} v2` })).toBeVisible()

  await admin.getByRole('link', { name: `${name} v2` }).click()
  await expect(admin).toHaveURL(/\/app\/categories\/\d+$/)
  await expect(admin.getByRole('main').getByRole('heading', { name: `${name} v2` })).toBeVisible()

  await admin.goto('/app/categories')
  const download = admin.waitForEvent('download')
  await admin.getByRole('button', { name: 'Export CSV' }).click()
  expect((await download).suggestedFilename()).toMatch(/^categories_export_.*\.csv$/)

  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/categories')
  await expect(cashier.getByRole('link', { name: 'Beverages' }).or(cashier.getByRole('row').nth(1))).toBeVisible()
  await expect(cashier.getByRole('button', { name: 'Add Category' })).toHaveCount(0)
  await expect(cashier.getByRole('button', { name: 'Edit' })).toHaveCount(0)
})

test('display order: add a product, reorder it with the buttons, save, and the order survives a reload', async ({ browser }) => {
  const admin = await (await browser.newContext()).newPage()
  await signIn(admin, /Layla/, '1234', '/product-display-order')
  await expect(admin.getByRole('main').getByRole('heading', { name: 'Product Display Order Management' })).toBeVisible()

  await admin.getByRole('button', { name: 'Add Product' }).first().click()
  const dialog = admin.getByRole('dialog')
  for (let i = 0; i < 2; i++) {
    // an added product leaves the picker, so the first "Add" is always a different one
    await dialog.getByRole('button', { name: 'Add', exact: true }).first().click()
    await expect(admin.getByText('Product added to display order').first()).toBeVisible()
    await admin.waitForTimeout(400)
  }
  await dialog.getByRole('button', { name: 'Close' }).click()

  const names = () => admin.locator('tbody tr strong').allTextContents()
  const before = await names()
  expect(before.length).toBeGreaterThan(0)
  await admin.getByRole('button', { name: 'Move up' }).last().click()
  await expect(admin.getByRole('button', { name: 'Save Order' })).toBeEnabled()
  const moved = await names()
  await admin.getByRole('button', { name: 'Save Order' }).click()
  await expect(admin.getByText('Display order updated successfully')).toBeVisible()

  await admin.reload()
  await expect(admin.locator('tbody tr strong').first()).toBeVisible()
  expect(await names()).toEqual(moved)

  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/product-display-order')
  await expect(cashier).toHaveURL(/\/app\/forbidden/)
})
