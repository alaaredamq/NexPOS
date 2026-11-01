import { expect, test, type Page } from '@playwright/test'

// Track C1 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5057 npx playwright test e2e/products.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin (no Products.Delete in the seeded role), sara / 1111 = Cashier (view only).

async function signIn(page: Page, name: RegExp, pin: string) {
  await page.goto('/app/login')
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

const unique = () => Date.now().toString(36)

test('create, find, view, edit, label and import a product', async ({ page }) => {
  await signIn(page, /Layla/, '1234')
  await page.goto('/app/products')
  await expect(page.getByRole('main').getByRole('heading', { name: 'Products' })).toBeVisible()
  await expect(page.getByText('Total Products')).toBeVisible()

  // create
  const tag = unique()
  const barcode = `E2E${tag}`
  await page.getByRole('button', { name: 'Add Product' }).click()
  await expect(page).toHaveURL(/\/app\/products\/new$/)
  await page.getByLabel(/Product Name/).fill(`E2E cable ${tag}`)
  await page.getByLabel(/^Barcode/).fill(barcode)
  await page.getByRole('combobox', { name: /^Category/ }).click()
  await page.getByRole('option').first().click()
  await page.getByLabel(/Purchase Price/).fill('4')
  await page.getByLabel(/Selling Price/).fill('6.5')
  await page.getByRole('button', { name: 'Add price' }).click()
  await page.getByLabel('Price name').fill('Wholesale')
  await page.getByLabel('Price', { exact: true }).fill('5')
  await page.getByRole('button', { name: 'Create Product' }).click()

  // find
  await expect(page).toHaveURL(/\/app\/products$/)
  await page.getByPlaceholder(/Search by name/).fill(barcode)
  const row = page.getByRole('row', { name: new RegExp(`E2E cable ${tag}`) })
  await expect(row).toBeVisible()
  await expect(page).toHaveURL(new RegExp(`q=${barcode}`))

  // view
  await row.getByText(`E2E cable ${tag}`).click()
  await expect(page.getByRole('main').getByRole('heading', { name: `E2E cable ${tag}` })).toBeVisible()
  await expect(page.getByText('Wholesale', { exact: true })).toBeVisible()

  // edit
  await page.getByRole('button', { name: 'Edit Product' }).first().click()
  await page.getByLabel(/Selling Price/).fill('7.25')
  await page.getByRole('button', { name: 'Save changes' }).click()
  await expect(page.getByText(/7\.25/).first()).toBeVisible()

  // label: a plain browser downloads a PDF
  await page.getByRole('button', { name: 'Print Label' }).click()
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: /Download 1 label/ }).click()
  expect((await download).suggestedFilename()).toMatch(/\.pdf$/i)

  // import: check first (saves nothing), then import
  await page.goto('/app/products')
  await page.getByRole('button', { name: 'Import' }).click()
  const newBarcode = `IMP${tag}`
  await page.locator('input[type=file]').setInputFiles({
    name: 'products.csv',
    mimeType: 'text/csv',
    // A new row needs a category and a supplier that exists ("Al Amal Wholesale" is in the demo data; it has no "No Supplier").
    buffer: Buffer.from(
      [
        'Barcode,Name,Category,Supplier,Selling Price',
        `${barcode},E2E renamed ${tag},,,8`,
        `${newBarcode},E2E imported ${tag},E2E import category,Al Amal Wholesale,3`,
      ].join('\n'),
    ),
  })
  await page.getByRole('button', { name: 'Check file' }).click()
  await expect(page.getByText('Nothing has been saved yet')).toBeVisible()
  await page.getByRole('button', { name: /^Import \(2\)$/ }).click()
  await expect(page.getByText('Import finished.')).toBeVisible()
  await page.getByRole('dialog').getByRole('button', { name: 'Close' }).last().click()
  await page.getByPlaceholder(/Search by name/).fill(tag)
  await expect(page.getByRole('row', { name: new RegExp(`E2E renamed ${tag}`) })).toBeVisible()
  await expect(page.getByRole('row', { name: new RegExp(`E2E imported ${tag}`) })).toBeVisible()
})

test('a cashier can look but not add, edit or import', async ({ page }) => {
  await signIn(page, /Sara/, '1111')
  await page.goto('/app/products')
  await expect(page.getByRole('main').getByRole('heading', { name: 'Products' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Add Product' })).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Import' })).toHaveCount(0)
  await page.goto('/app/products/new')
  await expect(page).toHaveURL(/\/app\/forbidden$/)
})
