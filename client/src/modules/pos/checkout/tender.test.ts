import { beforeEach, describe, expect, it } from 'vitest'
import type { PosCartInput } from '../store/cartStore'
import { changeDue, forgetAttempt, isTotalAcceptable, keyForSale, shortBy, tenderSuggestions, totalTolerance, typedDiscount } from './tender'

const cart = (quantity = 1): PosCartInput => ({ currencyId: 1, lines: [{ productId: 1, quantity }] })
const sale = (overrides: Partial<Parameters<typeof keyForSale>[0]> = {}) => ({
  cart: cart(),
  customerId: null,
  paymentMethodId: 1,
  addToDebt: false,
  totalAmount: 10,
  ...overrides,
})

beforeEach(() => forgetAttempt())

describe('idempotency key', () => {
  it('is the same for a retry of the same sale', () => {
    expect(keyForSale(sale())).toBe(keyForSale(sale()))
  })

  it('changes when the cart, customer, method, debt flag or total changes', () => {
    const first = keyForSale(sale())
    expect(keyForSale(sale({ cart: cart(2) }))).not.toBe(first)
    const second = keyForSale(sale({ cart: cart(2) }))
    expect(keyForSale(sale({ customerId: 5 }))).not.toBe(second)
    expect(keyForSale(sale({ paymentMethodId: 2 }))).not.toBe(keyForSale(sale({ paymentMethodId: 1 })))
    expect(keyForSale(sale({ totalAmount: 9 }))).not.toBe(keyForSale(sale({ totalAmount: 10 })))
  })

  it('ignores the payment method on a sale on account (the server picks its own)', () => {
    const debt = keyForSale(sale({ addToDebt: true, customerId: 3, paymentMethodId: 1 }))
    expect(keyForSale(sale({ addToDebt: true, customerId: 3, paymentMethodId: 2 }))).toBe(debt)
  })

  it('is new after a sale went through, even for an identical cart', () => {
    const before = keyForSale(sale())
    forgetAttempt()
    expect(keyForSale(sale())).not.toBe(before)
  })

  it('is at least 8 characters (the API minimum)', () => {
    expect(keyForSale(sale()).length).toBeGreaterThanOrEqual(8)
  })
})

describe('typed total', () => {
  it('allows the greater of 1.00 and 2 % either way', () => {
    expect(totalTolerance(10)).toBe(1)
    expect(totalTolerance(100)).toBe(2)
    expect(isTotalAcceptable(9, 10)).toBe(true)
    expect(isTotalAcceptable(8.99, 10)).toBe(false)
    expect(isTotalAcceptable(102, 100)).toBe(true)
    expect(isTotalAcceptable(102.01, 100)).toBe(false)
    expect(isTotalAcceptable(859_200 - 17_000, 859_200)).toBe(true)
    expect(isTotalAcceptable(859_200 - 17_300, 859_200)).toBe(false)
  })

  it('refuses a negative or non-numeric total', () => {
    expect(isTotalAcceptable(-0.5, 0.5)).toBe(false)
    expect(isTotalAcceptable(Number.NaN, 10)).toBe(false)
  })

  it('reads a lower total as a discount off the subtotal, as the receipt will print it', () => {
    expect(typedDiscount(9, 10, 10)).toEqual({ amount: 1, percent: 10 })
    // the cart already carried a 2.00 discount: 8.00 was proposed, 7.00 typed -> 3.00 off 10.00 = 30 %
    expect(typedDiscount(7, 8, 10)).toEqual({ amount: 3, percent: 30 })
  })

  it('is not a discount when the total is equal, higher or the subtotal is empty', () => {
    expect(typedDiscount(10, 10, 10)).toBeNull()
    expect(typedDiscount(10.5, 10, 10)).toBeNull()
    expect(typedDiscount(0, 0, 0)).toBeNull()
  })
})

describe('cash', () => {
  it('works out the change and the shortfall, never negative', () => {
    expect(changeDue(20, 13.5)).toBe(6.5)
    expect(changeDue(10, 13.5)).toBe(0)
    expect(shortBy(10, 13.5)).toBe(3.5)
    expect(shortBy(20, 13.5)).toBe(0)
    expect(changeDue(null, 13.5)).toBe(0)
  })

  it('suggests the exact amount, then round figures above it', () => {
    expect(tenderSuggestions(13.5)).toEqual([13.5, 14, 15, 20])
    expect(tenderSuggestions(4)).toEqual([4, 5, 10, 20])
  })

  it('scales to large currencies', () => {
    const figures = tenderSuggestions(859_200)
    expect(figures[0]).toBe(859_200)
    expect(figures.slice(1).every((f) => f > 859_200 && f % 1000 === 0)).toBe(true)
    expect(figures).toContain(900_000)
  })

  it('suggests nothing for an empty total', () => {
    expect(tenderSuggestions(0)).toEqual([])
  })
})
