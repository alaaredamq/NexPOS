import type { PosCartInput } from '../store/cartStore'
import { roundHalfEven } from '@/ui/format'

/**
 * Pure checkout logic (no React, no network): the idempotency key of an attempt, the discount a lowered total amounts to, the same
 * tolerance the server applies to a typed total, and the quick "cash received" amounts. Money is still decided by the server: these
 * only keep the dialog honest before it asks.
 */

// ---- idempotency -----------------------------------------------------------------------------------------------------------

export interface SaleFingerprintInput {
  cart: PosCartInput
  customerId: number | null
  paymentMethodId: number
  addToDebt: boolean
  totalAmount: number
}

/** What makes two attempts "the same sale": everything that changes money (the server fingerprints the same fields). */
export function saleFingerprint(sale: SaleFingerprintInput): string {
  return JSON.stringify([sale.cart, sale.customerId, sale.addToDebt ? 0 : sale.paymentMethodId, sale.addToDebt, sale.totalAmount])
}

let attempt: { fingerprint: string; key: string } | null = null

function newKey(): string {
  const crypto = globalThis.crypto
  if (crypto?.randomUUID) return crypto.randomUUID()
  return `k${Date.now().toString(36)}${Math.random().toString(36).slice(2, 12)}`
}

/**
 * The idempotency key for this sale. The same sale gets the same key again, so retrying after a lost response (or after fixing a refusal
 * such as "sell anyway") cannot sell twice; a changed cart / customer / method / amount is a new sale and gets a new key (the server
 * answers 409 `idempotency_key_reused` for a different cart under an old key).
 */
export function keyForSale(sale: SaleFingerprintInput): string {
  const fingerprint = saleFingerprint(sale)
  if (attempt?.fingerprint !== fingerprint) attempt = { fingerprint, key: newKey() }
  return attempt.key
}

/** After a sale went through (or the cashier started over): the next sale never reuses the key. */
export function forgetAttempt(): void {
  attempt = null
}

// ---- the typed total -------------------------------------------------------------------------------------------------------

/** The most the typed total may be raised above the server's (cash rounding): the greater of 1.00 and 2 % (`POSCheckoutService`). */
export function totalTolerance(serverTotal: number): number {
  return Math.max(1, serverTotal * 0.02)
}

/**
 * True when `typed` is a figure the server will take for a cart that prices at `serverTotal`. Lowering is the cashier's call (it is a
 * discount, shown on the receipt) so anything from 0 down is fine; only a raise is bounded.
 */
export function isTotalAcceptable(typed: number, serverTotal: number): boolean {
  return Number.isFinite(typed) && typed >= 0 && typed - serverTotal <= totalTolerance(serverTotal) + 1e-9
}

/**
 * Typing a lower total is a discount, and the receipt will say so: money off the subtotal, and what that is as a percentage of it
 * (together with any discount the cart already carried). Null when the typed total is not lower than the priced one.
 */
export function typedDiscount(typed: number, serverTotal: number, subtotal: number): { amount: number; percent: number } | null {
  if (!Number.isFinite(typed) || !(serverTotal - typed >= 0.01) || !(subtotal > 0)) return null
  const amount = subtotal - typed
  return { amount, percent: (amount / subtotal) * 100 }
}

// ---- cash ------------------------------------------------------------------------------------------------------------------

/** Change due for a cash payment. Never negative: a smaller amount than the total is "short", not a negative change. */
export function changeDue(received: number | null, total: number): number {
  if (received === null || !Number.isFinite(received)) return 0
  return Math.max(0, roundHalfEven(received - total, 2))
}

export function shortBy(received: number | null, total: number): number {
  if (received === null || !Number.isFinite(received)) return 0
  return Math.max(0, roundHalfEven(total - received, 2))
}

const NICE = [1, 2, 5]

/**
 * Round amounts a customer is likely to hand over: the total itself, then the next "nice" figures above it (1, 2, 5 × a power of ten),
 * scaled to the size of the total so a dollar sale offers 5 / 10 / 20 and a 859,200 pound sale offers 900,000 / 1,000,000 …
 * At most `count` figures, ascending, no duplicates, none below the total.
 */
export function tenderSuggestions(total: number, count = 4): number[] {
  if (!(total > 0)) return []
  const out = new Set<number>([roundHalfEven(total, 2)])
  const magnitude = 10 ** Math.max(0, Math.floor(Math.log10(total)) - 1)
  for (let exp = 0; out.size < count && exp < 8; exp++) {
    for (const n of NICE) {
      const step = n * magnitude * 10 ** exp
      const figure = Math.ceil(total / step - 1e-9) * step
      if (figure > total) out.add(roundHalfEven(figure, 2))
      if (out.size >= count) break
    }
  }
  return [...out].sort((a, b) => a - b).slice(0, count)
}
