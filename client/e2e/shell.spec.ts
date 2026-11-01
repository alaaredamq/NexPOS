import { expect, test, type Page } from '@playwright/test'

// Gate G0 as a spec. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5056 npx playwright test e2e/shell.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string) {
  if (!page.url().includes('/app/login')) await page.goto('/app/login') // keep ?returnTo= when we were redirected here
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click() // the picker shows when this PC has no remembered user
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
  // The till is where people land and it has no rail (full-screen page): these specs are about the shell, so step onto a page that has one.
  if (new URL(page.url()).pathname.endsWith('/pos')) await page.goto('/app/orders')
  await railLinks(page).first().waitFor() // the shell is mounted (its key handlers are live) before the spec presses anything
}

const railLinks = (page: Page) => page.getByRole('navigation', { name: /navigation|التنقل/i }).getByRole('link')

test('anonymous deep link goes to the login page and back after signing in', async ({ page }) => {
  await page.goto('/app/orders?x=1')
  await expect(page).toHaveURL(/\/app\/login\?returnTo=%2Forders%3Fx%3D1/)
  await signIn(page, /Sara/, '1111')
  await expect(page).toHaveURL(/\/app\/orders\?x=1$/)
})

test('the rail is filtered by role', async ({ browser }) => {
  const admin = await (await browser.newContext()).newPage()
  await signIn(admin, /Layla/, '1234')
  await expect(railLinks(admin).filter({ hasText: 'Users' })).toHaveCount(1)

  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111')
  await expect(railLinks(cashier).filter({ hasText: 'Orders' })).toHaveCount(1)
  await expect(railLinks(cashier).filter({ hasText: 'Users' })).toHaveCount(0)
  await expect(railLinks(cashier).filter({ hasText: 'Audit Log' })).toHaveCount(0)
})

test('EN to AR flips direction, relabels the rail and survives a reload; sign out ends the session', async ({ page }) => {
  await signIn(page, /Layla/, '1234')
  await page.getByRole('button', { name: 'عربي' }).click()
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
  await expect(railLinks(page).filter({ hasText: 'الطلبات' })).toHaveCount(1)
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl')
  await page.getByRole('button', { name: 'EN' }).click()

  await page.locator('button[aria-haspopup="menu"]').click()
  await page.getByRole('menuitem', { name: 'Logout' }).click()
  await expect(page).toHaveURL(/\/app\/login/)
  const status = await page.evaluate(() => fetch('/api/v1/session').then((r) => r.status))
  expect(status).toBe(401)
})

test('Ctrl+K opens the palette with only the pages of the session', async ({ page }) => {
  await signIn(page, /Sara/, '1111')
  await page.keyboard.press('Control+k')
  const dialog = page.getByRole('dialog')
  await expect(dialog).toBeVisible()
  await expect(dialog.getByRole('button', { name: /Orders/ })).toBeVisible()
  await expect(dialog.getByRole('button', { name: /Users/ })).toHaveCount(0)
})

test('idle timeout signs the user out and says why', async ({ page }) => {
  await page.clock.install()
  await signIn(page, /Layla/, '1234')
  await expect(railLinks(page).first()).toBeVisible() // the shell (and its idle timer) is mounted
  await page.clock.fastForward('31:00')
  await expect(page).toHaveURL(/\/app\/login\?.*reason=idle/)
  await expect(page.getByText(/signed out after 30 minutes/)).toBeVisible()
})
