import { expect, test, type Page } from '@playwright/test'

// Track I3 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5055 npx playwright test e2e/audit-logs.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('audit log: read the trail, filter, open an entry, follow its request, export - and a cashier cannot open it', async ({ browser }) => {
  const admin = await (await browser.newContext({ acceptDownloads: true })).newPage()
  await signIn(admin, /Layla/, '1234', '/audit-logs')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Audit Log', exact: true })).toBeVisible()
  await expect(main.getByText('Total entries')).toBeVisible()

  // Signing in wrote Security rows: filter to them and read the first one.
  await admin.getByRole('combobox', { name: 'Type', exact: true }).click()
  await admin.getByRole('option', { name: 'Security' }).click()
  await expect(admin).toHaveURL(/category=Security/)
  const first = main.getByRole('row').nth(1)
  await expect(first).toBeVisible()
  await first.click()

  const drawer = admin.getByRole('dialog', { name: /Audit entry/ })
  await expect(drawer).toBeVisible()
  await expect(admin).toHaveURL(/entry=\d+/)
  await expect(drawer.getByText('Where from, and how')).toBeVisible()

  // Follow the request: the type filter is dropped, the request chip appears, the drawer closes.
  const follow = drawer.getByRole('button', { name: /Everything in this request/ })
  if (await follow.count()) {
    await follow.click()
    await expect(admin.getByRole('dialog')).toHaveCount(0)
    await expect(admin).toHaveURL(/request=/)
    await expect(main.getByText(/^Request:/)).toBeVisible()
    await main.getByRole('button', { name: 'Remove this filter' }).click()
    await expect(admin).not.toHaveURL(/request=/)
  } else {
    await drawer.getByRole('button', { name: 'Close' }).first().click()
  }

  // The drill-down started from a clean slate, so the type filter is gone too.
  await expect(admin).not.toHaveURL(/category=/)

  const download = admin.waitForEvent('download')
  await admin.getByRole('button', { name: 'Export CSV' }).click()
  expect((await download).suggestedFilename()).toMatch(/^nexpos-audit-.*\.csv$/)

  // The cashier has no AuditLogs.Read: the route bounces to the forbidden page.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/audit-logs')
  await expect(cashier).toHaveURL(/forbidden/)
})
