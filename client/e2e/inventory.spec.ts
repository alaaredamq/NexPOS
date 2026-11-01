import { expect, test, type Page } from '@playwright/test'

// Track C3 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5061 npx playwright test e2e/inventory.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('inventory: adjust stock, set a minimum, read both reports, and a cashier is turned away', async ({ browser }) => {
  const admin = await (await browser.newContext({ acceptDownloads: true })).newPage()
  await signIn(admin, /Layla/, '1234', '/inventory')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Inventory Management' })).toBeVisible()

  // Adjust the first product: +7, with a reason.
  const firstRow = main.getByRole('row').nth(1)
  const name = (await firstRow.locator('strong').first().textContent())!.trim()
  const stockCell = firstRow.locator('[data-status], td').nth(3)
  const before = Number((await stockCell.textContent())!.replace(/[^\d.]/g, '').replace(/piece|kg|l$/i, '')) || 0
  await firstRow.getByRole('button', { name: 'Adjust Stock' }).click()
  const dialog = admin.getByRole('dialog', { name: 'Adjust Stock' })
  await dialog.getByLabel(/Quantity/).fill('7')
  await dialog.getByLabel('Reason for Adjustment').fill('e2e delivery')
  await expect(dialog.getByRole('status')).toContainText(String(before + 7))
  await dialog.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText(/Stock adjusted/)).toBeVisible()
  await expect(admin.getByRole('dialog')).toHaveCount(0)

  // Removing more than there is is blocked in the dialog.
  await admin.getByRole('searchbox').or(admin.getByRole('textbox', { name: /Search/ })).first().fill(name)
  const row = main.getByRole('row', { name: new RegExp(name) }).first()
  await row.getByRole('button', { name: 'Adjust Stock' }).click()
  await dialog.getByRole('button', { name: 'Remove Stock' }).click()
  await dialog.getByLabel(/Quantity/).fill('99999')
  await expect(dialog.getByRole('button', { name: 'Save' })).toBeDisabled()
  await dialog.getByRole('button', { name: 'Cancel' }).click()

  // Minimum stock.
  await row.getByRole('button', { name: 'Set Minimum Stock' }).click()
  const minimum = admin.getByRole('dialog', { name: 'Set Minimum Stock' })
  await minimum.getByLabel(/Minimum Stock Level/).fill('3')
  await minimum.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText('Minimum stock level updated successfully')).toBeVisible()

  // Reports.
  await admin.getByRole('link', { name: 'Low Stock Report' }).click()
  await expect(admin).toHaveURL(/\/app\/inventory\/low-stock$/)
  await expect(main.getByRole('heading', { name: 'Low Stock Report' })).toBeVisible()
  const lowPdf = admin.waitForEvent('download')
  await main.getByRole('button', { name: 'Download PDF' }).click()
  expect((await lowPdf).suggestedFilename()).toMatch(/\.pdf$/i)
  await main.getByRole('button', { name: 'Print on receipt printer' }).click()
  await expect(admin.getByText('Sent to the receipt printer')).toBeVisible()

  await admin.goto('/app/inventory/stock-value')
  await expect(main.getByRole('heading', { name: 'Stock Value Report' })).toBeVisible()
  await expect(main.getByRole('region', { name: 'Stock Value by Category' })).toBeVisible()
  await expect(main.getByRole('region', { name: 'Top 10 Products by Value' })).toBeVisible()
  const valuePdf = admin.waitForEvent('download')
  await main.getByRole('button', { name: 'Download PDF' }).click()
  expect((await valuePdf).suggestedFilename()).toMatch(/\.pdf$/i)

  // The cashier holds no inventory permission.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/inventory')
  await expect(cashier).toHaveURL(/\/app\/forbidden$/)
})

test('suppliers: create, record debt both ways, read the ledger, edit, export', async ({ browser }) => {
  const admin = await (await browser.newContext({ acceptDownloads: true })).newPage()
  await signIn(admin, /Layla/, '1234', '/suppliers')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Suppliers', exact: true })).toBeVisible()

  const name = `E2E Supplier ${Date.now()}`
  await admin.getByRole('button', { name: 'Add Supplier' }).click()
  const form = admin.getByRole('dialog', { name: 'Add Supplier' })
  await form.getByLabel(/Supplier Name/).fill(name)
  await form.getByLabel('Phone Number').fill('70123456')
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText('Supplier created successfully!')).toBeVisible()

  await admin.getByRole('searchbox').or(admin.getByRole('textbox', { name: /Search/ })).first().fill(name)
  const row = main.getByRole('row', { name: new RegExp(name) })
  await expect(row).toBeVisible()

  // "I owe them more" 120, then "I paid them" 200: the supplier ends up owing the shop 80.
  await row.getByRole('button', { name: 'Add Transaction' }).click()
  let debt = admin.getByRole('dialog', { name: 'Add Transaction' })
  await debt.getByLabel(/increases by/).fill('120')
  await debt.getByLabel('Note').fill('first delivery')
  await debt.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText('Debt updated successfully!')).toBeVisible()
  await expect(row).toContainText('120.00')

  await row.getByRole('button', { name: 'Add Transaction' }).click()
  debt = admin.getByRole('dialog', { name: 'Add Transaction' })
  await debt.getByRole('button', { name: /I paid them/ }).click()
  await debt.getByLabel(/decreases by/).fill('200')
  await expect(debt.getByRole('status')).toContainText('80.00')
  await debt.getByRole('button', { name: 'Save' }).click()
  await expect(row).toContainText('80.00')

  await row.getByRole('link', { name: name }).click()
  await expect(admin).toHaveURL(/\/app\/suppliers\/\d+$/)
  await expect(main.getByRole('heading', { name })).toBeVisible()
  await expect(main.getByText(/They owe you/)).toBeVisible()
  await expect(main.getByText('first delivery')).toBeVisible()
  await expect(main.getByRole('row')).toHaveCount(3) // header + two ledger rows

  await main.getByRole('button', { name: 'Edit Supplier' }).click()
  const edit = admin.getByRole('dialog', { name: 'Edit Supplier' })
  await edit.getByLabel(/Supplier Name/).fill(`${name} v2`)
  await edit.getByRole('button', { name: 'Save' }).click()
  await expect(main.getByRole('heading', { name: `${name} v2` })).toBeVisible()
  await expect(main.getByText(/They owe you/)).toBeVisible() // an edit never touches the balance

  await admin.goto('/app/suppliers')
  const download = admin.waitForEvent('download')
  await admin.getByRole('button', { name: 'Export CSV' }).click()
  expect((await download).suggestedFilename()).toMatch(/^suppliers_export_.*\.csv$/)

  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/suppliers')
  await expect(cashier).toHaveURL(/\/app\/forbidden$/)
})
