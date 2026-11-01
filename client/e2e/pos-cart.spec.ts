import { expect, test, type Page } from '@playwright/test'

// Track P3 happy path, on the dev-only host of the cart (/app/_pos-cart, Vite dev server) against the isolated demo DB:
//   BASE_URL=http://localhost:5175 npx playwright test e2e/pos-cart.spec.ts      (dev server proxying /api to the demo host)
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier.

async function signIn(page: Page, name: RegExp, pin: string, returnTo: string) {
  await page.goto(`/app/login?returnTo=${encodeURIComponent(returnTo)}`)
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

test('cart: add, change quantity, discount, price check by the server, hold and retrieve', async ({ page }) => {
  await signIn(page, /Sara/, '1111', '/_pos-cart')
  const cart = page.getByRole('region', { name: 'Shopping Cart' })
  await expect(cart.getByText('Cart is Empty')).toBeVisible()

  // a counted product (the tiles show "<notation> <price>" without a unit)
  await page.getByRole('list', { name: 'Products' }).getByRole('button').first().click()
  await expect(cart.getByRole('listitem')).toHaveCount(1)

  // the server prices it: checkout unlocks and the estimate marker goes away
  const checkout = cart.getByRole('button', { name: 'Checkout' })
  await expect(checkout).toBeEnabled()

  await cart.getByRole('button', { name: /Increase quantity of/ }).click()
  await expect(cart.getByRole('textbox', { name: 'Quantity' })).toHaveValue('2')
  await expect(checkout).toBeEnabled()
  const withTwo = await cart.getByTestId('cart-total').innerText()

  // 10 % off
  await cart.getByRole('button', { name: 'Discount' }).click()
  const discount = page.getByRole('dialog', { name: 'Apply Discount' })
  await discount.getByRole('textbox', { name: 'Percentage (%)' }).fill('10')
  await discount.getByRole('button', { name: 'Apply' }).click()
  await expect(cart.getByText(/10%/)).toBeVisible()
  await expect(checkout).toBeEnabled()
  expect(await cart.getByTestId('cart-total').innerText()).not.toBe(withTwo)

  // the price picker lists at least the default price, and closes without changing anything on Escape
  await cart.getByRole('button', { name: /Change the price of/ }).click()
  await expect(page.getByRole('dialog').getByRole('button', { name: /Retail/ })).toBeVisible()
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog')).toHaveCount(0)

  // hold it, the till is free, retrieve it again
  await cart.getByRole('button', { name: 'Hold' }).click()
  await page.getByRole('dialog', { name: 'Hold Cart' }).getByRole('button', { name: 'Hold Cart' }).click()
  await expect(cart.getByText('Cart is Empty')).toBeVisible()

  await cart.getByRole('button', { name: 'Retrieve' }).click()
  const held = page.getByRole('dialog', { name: 'Held Carts' })
  await held.getByRole('listitem').first().getByRole('button').first().click()
  await expect(cart.getByRole('listitem')).toHaveCount(1)
  await expect(cart.getByRole('textbox', { name: 'Quantity' })).toHaveValue('2')
  await expect(cart.getByText(/10%/)).toBeVisible()
  await expect(checkout).toBeEnabled()

  // clean up the held cart this test made
  await cart.getByRole('button', { name: 'Retrieve' }).click()
  await page.getByRole('dialog', { name: 'Held Carts' }).getByRole('button', { name: 'Delete All' }).click()
  await page.getByRole('button', { name: 'Yes, Delete All' }).click()
  await expect(page.getByRole('dialog', { name: 'Held Carts' }).getByText('No held carts found')).toBeVisible()
})
