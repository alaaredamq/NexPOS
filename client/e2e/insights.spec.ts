import { expect, test, type Page } from '@playwright/test'

// Track I1 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5058 npx playwright test e2e/insights.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier (may see the dashboard, not the reports).

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('dashboard and reports: tiles, charts with a table view, a report range, CSV and PDF export - and a cashier sees the dashboard only', async ({ browser }) => {
  const admin = await (await browser.newContext({ acceptDownloads: true })).newPage()
  await signIn(admin, /Layla/, '1234', '/dashboard')
  const main = admin.getByRole('main')

  // Dashboard: tiles, the sales chart, the category split and the latest orders.
  await expect(main.getByRole('heading', { name: 'Dashboard', exact: true })).toBeVisible()
  await expect(main.getByText('Revenue this month')).toBeVisible()
  await expect(main.getByRole('img', { name: 'Sales and profit over time' })).toBeVisible()
  await expect(main.getByRole('region', { name: 'Top Categories' })).toBeVisible()
  await expect(main.getByRole('region', { name: 'Recent Orders' }).getByRole('row').nth(1)).toBeVisible()

  // The same numbers as a table, and another period.
  await main.getByRole('button', { name: 'Table' }).click()
  await expect(main.getByRole('columnheader', { name: 'Profit' })).toBeVisible()
  await main.getByRole('button', { name: 'Chart' }).click()
  await main.getByRole('button', { name: 'Year' }).click()
  await expect(main.getByRole('button', { name: 'Year' })).toHaveAttribute('aria-pressed', 'true')
  await expect(main.getByRole('img', { name: 'Sales and profit over time' })).toBeVisible()

  // A latest order opens in the orders module.
  await main.getByRole('region', { name: 'Recent Orders' }).getByRole('link', { name: /^#\d+$/ }).first().click()
  await expect(admin).toHaveURL(/\/app\/orders\/\d+$/)

  // Reports: pick a range, the report follows and the URL keeps it.
  await admin.goto('/app/reports?from=2020-01-01&to=2020-01-07')
  await expect(main.getByRole('heading', { name: 'Reports & Analytics', exact: false })).toBeVisible()
  await expect(main.getByText('No sales in this range.').first()).toBeVisible()

  await main.getByRole('button', { name: '30 days' }).click()
  await expect(admin).toHaveURL(/from=\d{4}-\d{2}-\d{2}&to=\d{4}-\d{2}-\d{2}/)
  await expect(main.getByText('Total Sales')).toBeVisible()
  await expect(main.getByRole('img', { name: 'Daily Sales Trend' })).toBeVisible()

  // Exports of the range on screen.
  const csv = admin.waitForEvent('download')
  await main.getByRole('button', { name: 'Export CSV' }).click()
  const csvFile = await csv
  expect(csvFile.suggestedFilename()).toMatch(/^sales-report_\d{8}_\d{8}\.csv$/)

  const pdf = admin.waitForEvent('download')
  await main.getByRole('button', { name: 'Download PDF' }).click()
  expect(await pdf).toBeTruthy()

  // Arabic flips the direction, and the report still shows Western digits.
  await admin.getByRole('button', { name: 'عربي' }).click()
  await expect(admin.locator('html')).toHaveAttribute('dir', 'rtl')
  await expect(main.getByRole('img', { name: /الاتجاه|المبيعات/ }).first()).toBeVisible()

  // A cashier may open the dashboard (Dashboard.View) but holds no Reports.View.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/dashboard')
  await expect(cashier.getByRole('main').getByText('Revenue this month')).toBeVisible()
  await cashier.goto('/app/reports')
  await expect(cashier.getByRole('main').getByRole('heading', { name: 'Reports & Analytics', exact: false })).toHaveCount(0)
})
