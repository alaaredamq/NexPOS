import { expect, test, type Page } from '@playwright/test'

// Track P2 happy path: the till's screen, catalog, search and scanner. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5058 npx playwright test e2e/pos.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.
// The checkout (P4) is not part of this track; the cart panel (P3) is hosted in the cart column.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('till: tiles, category chips, search, scan box and shortcuts', async ({ browser }) => {
  const page = await (await browser.newContext()).newPage()
  await signIn(page, /Sara/, '1111', '/pos')

  // full-screen page: no rail, the till's own header and the two columns
  await expect(page.getByRole('navigation', { name: 'Navigation' })).toHaveCount(0)
  await expect(page.getByRole('region', { name: 'Product Catalog' })).toBeVisible()
  const grid = page.getByTestId('product-grid')
  const tiles = grid.getByRole('button')
  await expect(tiles.first()).toBeVisible()
  const firstName = (await tiles.first().innerText()).split('\n')[0] ?? ''

  // the scan box is focused and typing anywhere lands in it
  await expect(page.getByRole('textbox', { name: 'Scan barcode' })).toBeFocused()

  // a tile press reaches the cart column
  await tiles.first().click()
  await expect(page.getByRole('region', { name: 'Shopping Cart' })).toContainText(firstName)
  await expect(page.getByRole('textbox', { name: 'Scan barcode' })).toBeFocused()

  // category chips filter the grid
  const chips = page.getByRole('group', { name: 'Category' })
  const chipCount = await chips.getByRole('button').count()
  expect(chipCount).toBeGreaterThan(1)
  await chips.getByRole('button').nth(1).click()
  await expect(chips.getByRole('button').nth(1)).toHaveAttribute('aria-pressed', 'true')
  await expect(tiles.first()).toBeVisible()

  // search: type, pick with the keyboard
  const search = page.getByRole('combobox', { name: 'Search' })
  await search.fill(firstName.slice(0, 3))
  await expect(page.getByRole('option').first()).toBeVisible()
  await search.press('ArrowDown')
  await search.press('Enter')
  await expect(search).toHaveValue('')

  // an unknown barcode is refused in place
  const scan = page.getByRole('textbox', { name: 'Scan barcode' })
  await scan.fill('0000000000000')
  await scan.press('Enter')
  await expect(page.getByRole('alert').filter({ hasText: 'Not found' })).toBeVisible()

  // a QR link is refused without a lookup
  await scan.fill('https://example.com')
  await scan.press('Enter')
  await expect(page.getByRole('alert').filter({ hasText: /link was scanned/i })).toBeVisible()

  // F3 opens the price check, Escape closes it
  await page.keyboard.press('F3')
  await expect(page.getByRole('dialog', { name: 'Price Check' })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)
})
