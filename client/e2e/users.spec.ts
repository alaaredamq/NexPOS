import { expect, test, type Page } from '@playwright/test'

// Track A1 happy path. Run against the .NET host serving the built SPA on the isolated demo DB:
//   BASE_URL=http://localhost:5058 npx playwright test e2e/users.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo = '/') {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

const press = async (page: Page, digits: string) => {
  for (const digit of digits) await page.getByRole('button', { name: digit, exact: true }).click()
}

test('users: create a role and a user, the new user is held on the change-PIN screen until they choose a PIN, an edited role reaches them at once - and a cashier cannot open the page', async ({ browser }) => {
  const admin = await (await browser.newContext()).newPage()
  await signIn(admin, /Layla/, '1234', '/users/roles')
  const main = admin.getByRole('main')
  await expect(main.getByRole('heading', { name: 'User Management' })).toBeVisible()

  // A role that may only look at customers.
  const stamp = Date.now().toString().slice(-8)
  const roleName = `E2E role ${stamp}`
  await admin.getByRole('button', { name: 'Add Role' }).click()
  let dialog = admin.getByRole('dialog', { name: 'Add Role' })
  await dialog.getByLabel(/Role Name/).fill(roleName)
  await dialog.getByRole('group', { name: 'Customers' }).getByRole('checkbox', { name: 'View', exact: true }).check()
  await expect(dialog.getByText(/^1 \/ \d+ selected$/)).toBeVisible()
  await dialog.getByRole('button', { name: 'Save Role' }).click()
  await expect(admin.getByText('Role created.')).toBeVisible()
  await expect(admin.getByRole('row', { name: new RegExp(roleName) })).toBeVisible()

  // A user in that role, with an admin-chosen PIN.
  await admin.goto('/app/users')
  const username = `e2e${stamp}`
  await admin.getByRole('button', { name: 'Add User' }).click()
  dialog = admin.getByRole('dialog', { name: 'Add User' })
  await dialog.getByLabel(/^Username/).fill(username)
  await dialog.getByLabel(/Email Address/).fill(`${username}@example.com`)
  await dialog.getByLabel(/First Name/).fill('E2E')
  await dialog.getByLabel(/Last Name/).fill('Person')
  await dialog.getByLabel(/Login PIN/).fill('4821')
  await dialog.getByRole('combobox', { name: /^Role/ }).click()
  await admin.getByRole('option', { name: roleName }).click()
  await dialog.getByRole('button', { name: 'Create User' }).click()
  await expect(admin.getByText('User created.')).toBeVisible()
  await admin.getByRole('searchbox').or(admin.getByRole('textbox', { name: /Search/ })).first().fill(username)
  const row = admin.getByRole('row', { name: new RegExp(username) })
  await expect(row).toBeVisible()
  await expect(row.getByText('Must change PIN')).toBeVisible()

  // The new user signs in with the PIN they were given and cannot get past the change-PIN screen without choosing their own.
  const person = await (await browser.newContext()).newPage()
  await person.goto('/app/login')
  await person.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const pick = person.getByRole('button', { name: /E2E Person/ })
  if (await pick.count()) await pick.first().click()
  await press(person, '4821')
  await person.waitForURL(/\/app\/account\/change-pin$/)
  await expect(person.getByText(/An administrator set your PIN/)).toBeVisible()
  await expect(person.getByRole('radio', { name: /Current PIN/ })).toHaveCount(0)
  await person.goto('/app/customers')
  await person.waitForURL(/\/app\/account\/change-pin$/)

  await press(person, '4821') // the same PIN is refused ...
  await press(person, '4821')
  await person.getByRole('button', { name: 'Update PIN' }).click()
  await expect(person.getByRole('alert').filter({ hasText: /different/i })).toBeVisible()
  await person.getByRole('radio', { name: /^New PIN/ }).click()
  await press(person, '7359') // ... a new one is accepted
  await press(person, '7359')
  await person.getByRole('button', { name: 'Update PIN' }).click()
  await expect(person.getByText('PIN updated.')).toBeVisible()
  await expect(person).not.toHaveURL(/change-pin/)

  // Customers is allowed by the role; Users is not.
  await person.goto('/app/customers')
  await expect(person.getByRole('main').getByRole('heading', { name: 'Customers', exact: true })).toBeVisible()
  await person.goto('/app/users')
  await expect(person).toHaveURL(/\/app\/forbidden$/)

  // The admin takes the permission away; the user's open session notices on its next request.
  await admin.goto('/app/users/roles')
  await admin.getByRole('searchbox').or(admin.getByRole('textbox', { name: /Search/ })).first().fill(roleName)
  await admin.getByRole('row', { name: new RegExp(roleName) }).getByRole('button', { name: 'Edit' }).click()
  dialog = admin.getByRole('dialog', { name: 'Edit Role' })
  await dialog.getByRole('group', { name: 'Customers' }).getByRole('checkbox', { name: 'View', exact: true }).uncheck()
  await dialog.getByRole('button', { name: 'Save Role' }).click()
  await expect(admin.getByText('Role updated.')).toBeVisible()
  await person.goto('/app/customers')
  await expect(person).toHaveURL(/\/app\/forbidden$/)

  // A cashier has neither Users.View nor the page.
  const cashier = await (await browser.newContext()).newPage()
  await signIn(cashier, /Sara/, '1111', '/users')
  await expect(cashier).toHaveURL(/\/app\/forbidden$/)
})
