import { expect, test, type Page } from '@playwright/test'

// Track A4 happy path. Run against the .NET host serving the built SPA on the isolated demo DB (no licence file there, so selling is stopped):
//   BASE_URL=http://localhost:5055 npx playwright test e2e/backup.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('backup + licence: take a backup, download it, schedule one, restore it, see the licence - all while selling is stopped; a cashier gets neither', async ({ browser }) => {
  const admin = await (await browser.newContext({ acceptDownloads: true })).newPage()
  await signIn(admin, /Layla/, '1234', '/backup')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'Backup & Restore Management' })).toBeVisible()

  // Selling is stopped on this host (no licence file), yet the backup screens open and work.
  const name = `e2e-${Date.now()}`
  await admin.getByRole('button', { name: 'Create Backup' }).first().click()
  const create = admin.getByRole('dialog', { name: 'Create New Backup' })
  await create.getByLabel('Backup Name').fill(name)
  await create.getByRole('button', { name: 'Create Backup' }).click()
  await expect(admin.getByText('Backup completed successfully')).toBeVisible()
  const row = main.getByRole('row', { name })
  await expect(row).toBeVisible()
  await expect(row.getByText('Completed')).toBeVisible()

  const download = admin.waitForEvent('download')
  await row.getByRole('button', { name: 'Download' }).click()
  expect((await download).suggestedFilename()).toMatch(/\.bak$/)

  await row.getByRole('button', { name: 'Validate' }).click()
  await expect(admin.getByText('Backup is valid')).toBeVisible()

  // A schedule.
  await main.getByRole('link', { name: 'Schedule' }).click()
  await main.getByRole('button', { name: 'Create Schedule' }).click()
  const form = admin.getByRole('dialog', { name: 'Create Schedule' })
  await form.getByLabel(/Schedule Name/).fill(`${name}-nightly`)
  await form.getByRole('button', { name: 'Save' }).click()
  await expect(admin.getByText('Schedule created successfully')).toBeVisible()
  await expect(main.getByRole('row', { name: new RegExp(`${name}-nightly`) })).toContainText('Daily at 02:00')

  // Restore it: the dialog asks first, names the backup, and the session survives (the demo users are in the backup).
  await main.getByRole('link', { name: 'Restore' }).click()
  await main.getByRole('combobox', { name: /Select Backup/ }).click()
  await admin.getByRole('option', { name: new RegExp(name) }).click()
  await main.getByRole('button', { name: 'Start Restore' }).click()
  const confirm = admin.getByRole('dialog', { name: 'Restore this backup?' })
  await expect(confirm).toContainText(name)
  await confirm.getByRole('button', { name: 'Restore' }).click()
  await expect(admin.getByText(/Restore completed/)).toBeVisible()
  await expect(admin).toHaveURL(/\/backup\/restore/)

  // The restore took a safety backup first, and the list shows it.
  await main.getByRole('link', { name: 'Backups' }).click()
  await expect(main.getByRole('row').filter({ hasText: 'Emergency Backup' }).first()).toBeVisible()

  // The licence screen opens although selling is stopped, shows this PC's id, and refuses a made-up key.
  await admin.goto('/app/license')
  await expect(admin.getByRole('main').getByRole('heading', { name: 'License', exact: true })).toBeVisible()
  await expect(admin.getByLabel('Machine ID')).toHaveValue(/^[A-Z0-9]{5}(-[A-Z0-9]{5}){3}$/)
  await admin.getByRole('textbox', { name: 'Activate or renew' }).fill('definitely not a licence key')
  await admin.getByRole('button', { name: 'Activate' }).click()
  await expect(admin.getByRole('alert')).toContainText('incomplete')

  // The cashier has neither Backup.View nor License.Manage.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/backup')
  await expect(cashier).toHaveURL(/forbidden/)
  await cashier.goto('/app/license')
  await expect(cashier).toHaveURL(/forbidden/)
})
