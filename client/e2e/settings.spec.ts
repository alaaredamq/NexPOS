import { expect, test, type Page } from '@playwright/test'

// Track A2 happy path. Run against the .NET host serving the built SPA on the isolated demo DB (NEXPOS_DEMO=1: its stand-in printer accepts every job):
//   BASE_URL=http://localhost:5058 npx playwright test e2e/settings.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

// 1x1 transparent PNG.
const PNG = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==', 'base64')

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('settings: edit the receipt details, test and save the printer, rebrand the app and undo it - a cashier cannot open the page', async ({ browser }) => {
  const admin = await (await browser.newContext()).newPage()
  await signIn(admin, /Layla/, '1234', '/settings')
  await expect(admin).toHaveURL(/\/app\/settings\/store$/) // /settings lands on the first tab
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Settings', exact: true })).toBeVisible()

  // Receipt details: edit, watch the preview follow, save, reload.
  const stamp = Date.now().toString().slice(-6)
  const storeName = `E2E Shop ${stamp}`
  await main.getByLabel(/Store Name/).fill(storeName)
  await main.getByLabel(/Footer Message/).fill('see you soon')
  await expect(main.getByRole('img', { name: 'Receipt Preview' }).getByText(storeName)).toBeVisible()
  await expect(main.getByRole('img', { name: 'Receipt Preview' }).getByText('see you soon')).toBeVisible()
  await main.getByRole('button', { name: 'Save Changes' }).click()
  await expect(admin.getByText('Settings saved successfully')).toBeVisible()
  await admin.reload()
  await expect(main.getByLabel(/Store Name/)).toHaveValue(storeName)

  // A blank name is refused on the field.
  await main.getByLabel(/Store Name/).fill('')
  await main.getByRole('button', { name: 'Save Changes' }).click()
  await expect(main.getByText('The store name is required.')).toBeVisible()
  await main.getByLabel(/Store Name/).fill(storeName)

  // Printers: pick the (demo) receipt printer, send a test slip, save with a new sticker size.
  await main.getByRole('link', { name: 'Printers & Labels' }).click()
  await main.getByRole('combobox', { name: /^Receipt Printer/ }).click()
  await admin.getByRole('option', { name: 'Demo Receipt Printer' }).click()
  await main.getByRole('button', { name: 'Test', exact: true }).click()
  await expect(admin.getByText('Test print sent successfully')).toBeVisible()
  await main.getByLabel(/Sticker Width/).fill('50')
  await main.getByRole('button', { name: 'Save Settings' }).click()
  await expect(admin.getByText('Printer settings saved')).toBeVisible()
  await admin.reload()
  await expect(main.getByRole('combobox', { name: /^Receipt Printer/ })).toHaveValue('Demo Receipt Printer')
  await expect(main.getByLabel(/Sticker Width/)).toHaveValue('50')

  // Branding: a name and a logo reach the shell at once; a bad file is refused; then undo.
  await main.getByRole('link', { name: 'Branding' }).click()
  await main.getByLabel('Favicon').setInputFiles({ name: 'icon.gif', mimeType: 'image/gif', buffer: PNG })
  await expect(main.getByText(/Unsupported file type '\.gif'/)).toBeVisible()
  await main.getByLabel('App name').fill(`Acme ${stamp}`)
  await main.getByLabel('Logo').setInputFiles({ name: 'logo.png', mimeType: 'image/png', buffer: PNG })
  await main.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(admin.getByText('Branding saved')).toBeVisible()
  await expect(admin.getByRole('navigation').first().getByText(`Acme ${stamp}`).first()).toBeVisible()
  await expect(main.getByLabel('Remove and use the default logo')).toBeVisible()

  await main.getByLabel('App name').fill('')
  await main.getByLabel('Remove and use the default logo').check()
  await main.getByRole('button', { name: 'Save', exact: true }).click()
  await expect(admin.getByText('Branding saved')).toBeVisible()
  await expect(main.getByLabel('Remove and use the default logo')).toHaveCount(0)

  // A cashier has no Settings.Manage.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/settings/store')
  await expect(cashier).toHaveURL(/\/app\/forbidden$/)
})
