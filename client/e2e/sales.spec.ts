import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

// Track S1 happy path. Run against the .NET host serving the built SPA on the isolated demo DB (licence enforcement off, so the till sells):
//   BASE_URL=http://localhost:5058 npx playwright test e2e/sales.spec.ts
// Demo users (--seed-demo): layla / 1234 = Admin, sara / 1111 = Cashier (can look at orders, nothing else here).

async function signIn(page: Page, name: RegExp, pin: string) {
  await page.goto('/app/login')
  await page.getByText(/Who is signing in\?|Welcome back/).first().waitFor()
  const person = page.getByRole('button', { name })
  if (await person.count()) await person.first().click()
  for (const digit of pin) await page.getByRole('button', { name: digit, exact: true }).click()
  await page.waitForURL((url) => !url.pathname.endsWith('/login'))
}

/** Sells one piece product through the till's API (the same path the shop uses) and returns what the back office will show. */
async function sell(request: APIRequestContext, quantity = 2) {
  const headers = { 'X-NexPOS-Client': 'web' }
  let productId = 0
  for (let id = 37; id <= 60 && !productId; id++) {
    const response = await request.get(`/api/v1/pos/products/${id}`)
    if (!response.ok()) continue
    const product = await response.json()
    if (product.unitKind === 'count' && !product.isPack && product.stockQuantity >= 10) productId = id
  }
  expect(productId, 'a piece product with stock').toBeGreaterThan(0)

  const cart = { currencyId: 1, lines: [{ productId, quantity }] }
  const priced = await (await request.post('/api/v1/pos/price', { headers, data: cart })).json()
  const response = await request.post('/api/v1/pos/sales/checkout', {
    headers,
    data: { idempotencyKey: crypto.randomUUID(), cart, paymentMethodId: 1, addToDebt: false, totalAmount: priced.total, printReceipt: false },
  })
  expect(response.ok(), await response.text()).toBe(true)
  const sale = await response.json()
  return { orderId: sale.orderId as number, orderNumber: `ORD-${String(sale.orderId).padStart(6, '0')}`, receiptNumber: sale.receiptNumber as string, receiptId: sale.receiptId as number, total: sale.total as number }
}

test('find, view, reprint, change, cancel and void a sale', async ({ page }) => {
  await signIn(page, /Layla/, '1234')
  const first = await sell(page.request)
  const second = await sell(page.request, 3)
  const third = await sell(page.request)

  // orders: find the sale and open it
  await page.goto('/app/orders')
  await expect(page.getByRole('main').getByRole('heading', { name: 'Orders Management' })).toBeVisible()
  await expect(page.getByText('Total Orders')).toBeVisible()
  await page.getByPlaceholder(/Search by order number/).fill(first.orderNumber)
  const row = page.getByRole('row', { name: new RegExp(first.orderNumber) })
  await expect(row).toBeVisible()
  await expect(page).toHaveURL(new RegExp(`q=${first.orderNumber}`))
  await row.getByText(first.orderNumber).click()
  await expect(page.getByRole('main').getByRole('heading', { name: first.orderNumber })).toBeVisible()
  await expect(page.getByText('Order Summary')).toBeVisible()
  await expect(page.getByRole('link', { name: first.receiptNumber })).toBeVisible()

  // a plain status change
  await page.getByRole('button', { name: 'Update Status' }).first().click()
  const dialog = page.getByRole('dialog', { name: 'Update Order Status' })
  await dialog.getByRole('combobox').click()
  await page.getByRole('option', { name: 'Shipped' }).click()
  await dialog.getByRole('button', { name: 'Update Status' }).click()
  await expect(page.getByText('Order status updated successfully')).toBeVisible()
  await expect(page.getByRole('main').getByText('Shipped').first()).toBeVisible()

  // a reprint answers (no printer in the demo: either toast or the PDF offer)
  await page.getByRole('button', { name: 'Print Receipt' }).first().click()
  await expect(page.getByText(/Sent to the receipt printer|Could not print/).first()).toBeVisible()
  const pdfOffer = page.getByRole('dialog').getByRole('button', { name: 'Download PDF instead' })
  if (await pdfOffer.count()) await page.getByRole('dialog').getByRole('button', { name: 'Cancel' }).click()

  // cancel another sale from the list: the whole sale is reversed
  await page.goto(`/app/orders?q=${second.orderNumber}`)
  await page.getByRole('row', { name: new RegExp(second.orderNumber) }).getByRole('button', { name: 'Update Status' }).click()
  const cancel = page.getByRole('dialog', { name: 'Update Order Status' })
  await cancel.getByRole('combobox').click()
  await page.getByRole('option', { name: 'Cancelled' }).click()
  await expect(cancel.getByRole('alert')).toContainText('reverses the whole sale')
  await cancel.getByRole('button', { name: 'Cancel order' }).click()
  await expect(page.getByText(`Order ${second.orderNumber} cancelled`)).toBeVisible()
  await expect(page.getByRole('row', { name: new RegExp(second.orderNumber) }).getByText('Cancelled')).toBeVisible()

  // receipts: its receipt is void now; void another one with a reason
  await page.goto(`/app/receipts?status=void&q=${second.receiptNumber}`)
  await expect(page.getByRole('row', { name: new RegExp(second.receiptNumber) })).toBeVisible()
  await page.goto(`/app/receipts?q=${third.receiptNumber}`)
  await page.getByRole('row', { name: new RegExp(third.receiptNumber) }).getByRole('button', { name: 'Void Receipt' }).click()
  const voidDialog = page.getByRole('dialog')
  await voidDialog.getByRole('button', { name: 'Void Receipt' }).click()
  await expect(voidDialog.getByText('Please enter a reason for voiding')).toBeVisible()
  await voidDialog.getByLabel(/Reason for Voiding/).fill('Scanned the wrong item')
  await voidDialog.getByRole('button', { name: 'Void Receipt' }).click()
  await expect(page.getByText('Receipt voided, order cancelled and items returned to stock')).toBeVisible()

  // its details show why
  await page.goto(`/app/receipts?status=void&q=${third.receiptNumber}`)
  await expect(page.getByText('Scanned the wrong item')).toBeVisible()
  await page.getByText(third.receiptNumber).click()
  await expect(page.getByText('This receipt has been voided.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Void Receipt' })).toHaveCount(0)
  await page.getByRole('link', { name: /^ORD-/ }).click()
  await expect(page.getByRole('main').getByText('Cancelled').first()).toBeVisible()

  // the rail points at the SPA for both pages
  await expect(page.getByRole('link', { name: 'Receipts' }).first()).toHaveAttribute('href', '/app/receipts')
})

test('a cashier sees the orders but cannot change them, and the receipts are closed to them', async ({ page }) => {
  await signIn(page, /Sara/, '1111')
  await page.goto('/app/orders')
  await expect(page.getByRole('main').getByRole('heading', { name: 'Orders Management' })).toBeVisible()
  await expect(page.getByRole('button', { name: 'Update Status' })).toHaveCount(0)

  await page.goto('/app/receipts')
  await expect(page).toHaveURL(/\/app\/forbidden$/)
})
