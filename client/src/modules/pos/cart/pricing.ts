import { roundHalfEven } from '@/ui/format'
import type { CartContent, CartDiscount, CartLine, PosCartInput, PosCurrency, PosPricedCart } from '../store/cartStore'

/**
 * The till's price arithmetic, mirrored from `PosPricingMath` (C#) — track P3.
 *
 * This is only ever a PREVIEW: it keeps the cart readable while the server's `pos/price` is on its way (and offline-ish blips).
 * The golden file `docs/react-migration/golden/pos-pricing.json` is run against these functions (`pricing.test.ts`), the same file
 * the C# tests run, so the two cannot drift apart unnoticed. Money is never decided here: a sale sends the server's total.
 */

type Rated = Pick<PosCurrency, 'id' | 'rateToUsd'>

/** Unit price converted between currencies, exactly like the server: same currency untouched, otherwise through USD and banker's-rounded to 2 dp. */
export function convertAmount(amount: number, fromId: number, toId: number, currencies: readonly Rated[]): number {
  if (fromId === toId) return amount
  const from = currencies.find((c) => c.id === fromId)
  const to = currencies.find((c) => c.id === toId)
  // An unknown currency cannot be converted: show the amount as is rather than NaN (the server's answer replaces it anyway).
  if (!from || !to || !(from.rateToUsd > 0)) return amount
  return roundHalfEven((amount / from.rateToUsd) * to.rateToUsd, 2)
}

/** Unit price × (weight or volume, else pieces). Not rounded — a line total keeps its decimals, only the cart total is rounded. */
export function lineTotal(unitPrice: number, quantity: number, weightOrVolume: number | null): number {
  return unitPrice * (weightOrVolume !== null && weightOrVolume > 0 ? weightOrVolume : quantity)
}

/** The unit price now in force for a line, in the cart currency. */
export function lineUnitPrice(line: CartLine, cartCurrencyId: number, currencies: readonly Rated[]): number {
  const price = line.price
  if (price.kind === 'default') return convertAmount(line.sellingPrice, line.currencyId, cartCurrencyId, currencies)
  return convertAmount(price.amount, price.currencyId, cartCurrencyId, currencies)
}

export interface DiscountBreakdown {
  /** Money off, in the cart currency. */
  amount: number
  /** The same discount as a percentage of the subtotal. */
  percentage: number
}

/** A discount as money AND as a percentage, whichever way it was entered. A percentage wins over an amount (as on the server). */
export function discountBreakdown(
  subtotal: number,
  discount: CartDiscount | null,
  cartCurrencyId: number,
  currencies: readonly Rated[],
): DiscountBreakdown {
  if (!discount || !(discount.value > 0) || !(subtotal > 0)) return { amount: 0, percentage: 0 }
  if (discount.type === 'percentage') return { amount: (subtotal * discount.value) / 100, percentage: discount.value }
  const amount = convertAmount(discount.value, discount.currencyId, cartCurrencyId, currencies)
  return { amount, percentage: (amount / subtotal) * 100 }
}

export interface LinePreview {
  unitPrice: number
  lineTotal: number
}

export interface CartPreview {
  lines: Map<number, LinePreview>
  subtotal: number
  discountAmount: number
  discountPercentage: number
  total: number
}

/** The whole cart priced locally. */
export function previewCart(
  content: Pick<CartContent, 'lines' | 'discount'>,
  cartCurrencyId: number,
  currencies: readonly Rated[],
): CartPreview {
  const lines = new Map<number, LinePreview>()
  let subtotal = 0
  for (const line of content.lines) {
    const unitPrice = lineUnitPrice(line, cartCurrencyId, currencies)
    const total = lineTotal(unitPrice, line.quantity, line.weightOrVolume)
    lines.set(line.productId, { unitPrice, lineTotal: total })
    subtotal += total
  }
  const { amount, percentage } = discountBreakdown(subtotal, content.discount, cartCurrencyId, currencies)
  return {
    lines,
    subtotal,
    discountAmount: amount,
    discountPercentage: percentage,
    total: Math.max(0, roundHalfEven(subtotal - amount, 2)),
  }
}

/**
 * The cart as `pos/price`, `carts` (hold) and `pos/sales/checkout` take it. Nothing in it is a price the browser decided: a stored
 * price is sent by id, a typed one as `customPrice` (converted into the cart currency; the server re-checks it against cost).
 */
export function buildCartInput(
  content: Pick<CartContent, 'lines' | 'discount'>,
  cartCurrencyId: number,
  currencies: readonly Rated[],
): PosCartInput {
  const input: PosCartInput = {
    currencyId: cartCurrencyId,
    lines: content.lines.map((line) => ({
      productId: line.productId,
      quantity: line.quantity,
      ...(line.weightOrVolume !== null ? { weightOrVolume: line.weightOrVolume } : {}),
      ...(line.price.kind === 'tier' ? { priceTierId: line.price.tierId } : {}),
      ...(line.price.kind === 'custom'
        ? { customPrice: convertAmount(line.price.amount, line.price.currencyId, cartCurrencyId, currencies) }
        : {}),
    })),
  }
  const { discount } = content
  if (discount && discount.value > 0) {
    if (discount.type === 'percentage') input.discountPercentage = discount.value
    else input.discountAmount = convertAmount(discount.value, discount.currencyId, cartCurrencyId, currencies)
    if (discount.label) input.discountLabel = discount.label
  }
  return input
}

/** Two carts that must be priced the same way have the same key (it is the TanStack Query key of the price call). */
export function cartInputKey(input: PosCartInput): string {
  return JSON.stringify(input)
}

/** Totals of a priced cart in another currency, for the "LBP and USD side by side" readout. Display only. */
export function totalInCurrency(priced: Pick<PosPricedCart, 'total' | 'currencyId'>, toId: number, currencies: readonly Rated[]): number {
  return convertAmount(priced.total, priced.currencyId, toId, currencies)
}
