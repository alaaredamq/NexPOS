import { expect, test, type Page } from '@playwright/test'

// Track I2 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5061 npx playwright test e2e/notifications.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

/** Notifications and alerts are raised by other code in the app; the old page's create endpoints are the way to get some into a demo DB. */
async function seed(page: Page, tag: string) {
  const notification = await page.request.post('/en/Notifications/CreateNotification', {
    data: { title: `${tag} backup`, message: `${tag} finished`, type: 'success', priority: 'normal' },
  })
  expect(notification.ok()).toBe(true)
  const alert = await page.request.post('/en/Notifications/CreateAlert', {
    data: { title: `${tag} alert`, message: `${tag} needs a look`, type: 'low_stock' },
  })
  expect(alert.ok()).toBe(true)
}

test('notifications: the bell shows the news, the page works them, and a cashier sees no alerts', async ({ browser }) => {
  const admin = await (await browser.newContext()).newPage()
  await signIn(admin, /Layla/, '1234', '/notifications')
  const tag = `E2E${Date.now()}`
  await seed(admin, tag)
  await admin.reload()

  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Notifications & Alerts' })).toBeVisible()
  await expect(main.getByText(`${tag} backup`, { exact: true })).toBeVisible()

  // The bell counts both, and a click on a notification marks it read.
  const bell = admin.getByRole('button', { name: /^Notifications \(/ })
  await expect(bell).toBeVisible()
  await main.getByPlaceholder('Search').fill(tag)
  const row = main.getByRole('listitem').filter({ hasText: `${tag} backup` })
  await expect(row).toHaveCount(1)
  await row.getByRole('button', { name: 'Mark as read' }).click()
  await expect(admin.getByText('Notification marked as read')).toBeVisible()
  await expect(row.getByRole('button', { name: 'Mark as read' })).toHaveCount(0)

  // Alerts tab: dismiss it, then find it again under Closed.
  await main.getByRole('button', { name: 'Alerts', exact: true }).click()
  await main.getByPlaceholder('Search').fill(tag)
  const alertRow = main.getByRole('listitem').filter({ hasText: `${tag} alert` })
  await expect(alertRow).toHaveCount(1)
  await alertRow.getByRole('button', { name: 'Dismiss alert' }).click()
  await expect(admin.getByText('Alert dismissed')).toBeVisible()
  await expect(alertRow).toHaveCount(0)
  await main.getByRole('button', { name: 'Closed' }).click()
  await expect(main.getByRole('listitem').filter({ hasText: `${tag} alert` })).toHaveCount(1)

  // The Arabic page flips to RTL.
  // (what choosing Arabic does: the stored choice and the cookie the old pages share)
  await admin.evaluate(() => {
    localStorage.setItem('nexpos.lang', 'ar')
    document.cookie = 'nexpos.lang=ar; path=/'
  })
  await admin.reload()
  await expect(admin.locator('html')).toHaveAttribute('dir', 'rtl')
  await expect(main.getByRole('heading', { name: 'الإشعارات والتنبيهات' })).toBeVisible()

  // A cashier has the notifications but no alerts tab, and the API refuses them.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/notifications')
  const cashierMain = cashier.getByRole('main')
  await expect(cashierMain.getByRole('heading', { name: 'Notifications & Alerts' })).toBeVisible()
  await expect(cashierMain.getByRole('button', { name: 'Alerts', exact: true })).toHaveCount(0)
  const refused = await cashier.request.get('/api/v1/notifications/alerts')
  expect(refused.status()).toBe(403)
})
