import { expect, test, type Page } from '@playwright/test'

// Track S2 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5058 npx playwright test e2e/cash-drawer.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier (no Sales.View, so no drawer).

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('drawer: open, drop cash, close against the count, read the Z report and find it in the history', async ({ browser }) => {
  const context = await browser.newContext({ acceptDownloads: true })
  const admin = await context.newPage()
  await signIn(admin, /Layla/, '1234', '/cash-drawer')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Cash Drawer Management' })).toBeVisible()

  // An earlier run may have left a session open: count it out so the test starts from a closed drawer.
  if (await main.getByRole('button', { name: 'Close Drawer' }).count()) {
    await main.getByRole('button', { name: 'Close Drawer' }).click()
    await admin.getByLabel(/Actual Cash in Drawer/).fill('0')
    await admin.getByLabel(/Closing Notes/).fill('e2e cleanup')
    await admin.getByRole('button', { name: 'Close Cash Drawer' }).click()
    const confirmDialog = admin.getByRole('dialog')
    if (await confirmDialog.count()) await confirmDialog.getByRole('button', { name: 'Close Cash Drawer' }).click()
    await expect(admin).toHaveURL(/\/app\/cash-drawer\/z-report\/\d+$/)
    await admin.goto('/app/cash-drawer')
  }

  await expect(main.getByText('No Open Cash Drawer Session')).toBeVisible()
  await expect(admin.getByText('Drawer not opened')).toBeVisible() // the shell chip

  await main.getByRole('button', { name: 'Open Cash Drawer' }).first().click()
  await admin.getByLabel(/Starting Cash Amount/).fill('100')
  await admin.getByRole('button', { name: 'Open Cash Drawer' }).click()
  await expect(admin.getByText('Cash drawer opened successfully!')).toBeVisible()
  await expect(main.getByText('Current Open Session')).toBeVisible()
  await expect(admin.getByText(/Drawer open/)).toBeVisible() // the chip follows at once, not after the 60 s refresh

  await main.getByRole('button', { name: 'Cash Drop' }).click()
  const dialog = admin.getByRole('dialog', { name: 'Record Cash Drop' })
  await dialog.getByLabel(/Amount/).fill('20')
  await dialog.getByRole('button', { name: 'Record Cash Drop' }).click()
  await expect(admin.getByText('Cash drop recorded successfully')).toBeVisible()
  await expect(main.getByText('Expected Cash').locator('..')).toContainText('80.00')

  await main.getByRole('button', { name: 'Close Drawer' }).click()
  await admin.getByLabel(/Actual Cash in Drawer/).fill('80')
  await expect(main.getByRole('status').filter({ hasText: 'Perfect Balance' })).toBeVisible()
  await admin.getByRole('button', { name: 'Close Cash Drawer' }).click()

  await expect(admin).toHaveURL(/\/app\/cash-drawer\/z-report\/(\d+)$/)
  const sessionId = /z-report\/(\d+)$/.exec(admin.url())![1]!
  await expect(admin.getByLabel('Z-Report', { exact: true }).getByText('PERFECT BALANCE!')).toBeVisible()
  const pdf = admin.waitForEvent('download')
  await admin.getByRole('button', { name: 'Download PDF' }).click()
  expect((await pdf).suggestedFilename()).toMatch(/\.pdf$/)

  await admin.goto('/app/cash-drawer/history')
  await expect(admin.getByRole('main').getByText(`#${sessionId}`)).toBeVisible()
  await expect(admin.getByText('Drawer not opened')).toBeVisible()

  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/cash-drawer')
  await expect(cashier).toHaveURL(/\/app\/forbidden$/)
})
