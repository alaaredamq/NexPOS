import { describe, expect, it } from 'vitest'
import { roundHalfEven } from '@/ui/format'
import golden from '../../../../../docs/react-migration/golden/pos-pricing.json'
import type { CartLine } from '../store/cartStore'
import { buildCartInput, convertAmount, discountBreakdown, lineTotal, previewCart } from './pricing'

// The very same cases the C# tests run (Tests/Api/Pos/PosPricingGoldenTests.cs): the till's preview must agree with the server.

describe('golden file: unit price between currencies', () => {
  it.each(golden.unitPrice)('$name', (c) => {
    const from = { id: 1, rateToUsd: Number(c.fromRate) }
    const to = { id: c.sameCurrency ? 1 : 2, rateToUsd: Number(c.toRate) }
    expect(convertAmount(Number(c.price), 1, to.id, [from, to])).toBeCloseTo(Number(c.expected), 9)
  })
})

describe('golden file: line total', () => {
  it.each(golden.lineTotal)('$name', (c) => {
    expect(lineTotal(Number(c.unitPrice), c.quantity, c.weightOrVolume === null ? null : Number(c.weightOrVolume))).toBeCloseTo(Number(c.expected), 9)
  })
})

describe('golden file: discount and total', () => {
  it.each(golden.discount)('$name', (c) => {
    const subtotal = Number(c.subtotal)
    // The golden "amount" is a flat discount; the cart currency is the one it was typed in.
    const discount =
      c.percentage !== null
        ? ({ type: 'percentage', value: Number(c.percentage), label: null } as const)
        : c.amount !== null
          ? ({ type: 'amount', value: Number(c.amount), currencyId: 1, label: null } as const)
          : null
    const { amount } = discountBreakdown(subtotal, discount, 1, [])
    expect(amount).toBeCloseTo(Number(c.expectedAmount), 9)
    expect(roundHalfEven(subtotal - amount, 2)).toBeCloseTo(Number(c.expectedTotal), 9)
  })
})

const usd = { id: 1, rateToUsd: 1 }
const lbp = { id: 2, rateToUsd: 89500 }

const line = (overrides: Partial<CartLine> = {}): CartLine => ({
  productId: 1,
  name: 'Cola',
  barcode: '',
  categoryId: 1,
  currencyId: 1,
  sellingPrice: 3.2,
  unit: null,
  unitKind: 'count',
  canChangePrice: true,
  stockQuantity: 10,
  quantity: 1,
  weightOrVolume: null,
  displayValue: null,
  displayUnit: null,
  price: { kind: 'default' },
  ...overrides,
})

describe('previewCart', () => {
  it('prices a USD product in an LBP cart through the rates', () => {
    const preview = previewCart({ lines: [line({ quantity: 3 })], discount: null }, 2, [usd, lbp])
    expect(preview.lines.get(1)).toEqual({ unitPrice: 286400, lineTotal: 859200 })
    expect(preview.total).toBe(859200)
  })

  it('sells a weighed line by its weight, not its piece count', () => {
    const preview = previewCart({ lines: [line({ sellingPrice: 11.5, unit: 'kg', unitKind: 'weight', weightOrVolume: 0.75 })], discount: null }, 1, [usd])
    expect(preview.subtotal).toBeCloseTo(8.625, 9)
    expect(preview.total).toBe(8.62) // 8.625 goes to the even neighbour, like Math.Round
  })

  it('uses a tier or custom price in the currency it was chosen in', () => {
    const tier = line({ price: { kind: 'tier', tierId: 7, label: 'Wholesale', amount: 286400, currencyId: 2 } })
    expect(previewCart({ lines: [tier], discount: null }, 1, [usd, lbp]).lines.get(1)?.unitPrice).toBeCloseTo(3.2, 9)
    const custom = line({ price: { kind: 'custom', amount: 2.5, currencyId: 1 } })
    expect(previewCart({ lines: [custom], discount: null }, 1, [usd, lbp]).total).toBe(2.5)
  })

  it('takes a flat discount in its own currency off the subtotal', () => {
    const preview = previewCart(
      { lines: [line({ sellingPrice: 100 })], discount: { type: 'amount', value: 89500, currencyId: 2, label: null } },
      1,
      [usd, lbp],
    )
    expect(preview.discountAmount).toBe(1)
    expect(preview.discountPercentage).toBe(1)
    expect(preview.total).toBe(99)
  })

  it('never goes below zero', () => {
    const preview = previewCart({ lines: [line()], discount: { type: 'amount', value: 50, currencyId: 1, label: null } }, 1, [usd])
    expect(preview.total).toBe(0)
  })
})

describe('buildCartInput', () => {
  it('sends ids and weights, never a price the browser decided', () => {
    const input = buildCartInput(
      {
        lines: [
          line({ productId: 1, quantity: 2 }),
          line({ productId: 2, unit: 'kg', unitKind: 'weight', weightOrVolume: 0.3, price: { kind: 'tier', tierId: 9, label: 'W', amount: 5, currencyId: 1 } }),
          line({ productId: 3, price: { kind: 'custom', amount: 286400, currencyId: 2 } }),
        ],
        discount: { type: 'percentage', value: 10, label: 'Staff' },
      },
      1,
      [usd, lbp],
    )
    expect(input).toEqual({
      currencyId: 1,
      lines: [
        { productId: 1, quantity: 2 },
        { productId: 2, quantity: 1, weightOrVolume: 0.3, priceTierId: 9 },
        { productId: 3, quantity: 1, customPrice: 3.2 },
      ],
      discountPercentage: 10,
      discountLabel: 'Staff',
    })
  })

  it('sends a flat discount converted to the cart currency', () => {
    const input = buildCartInput({ lines: [line()], discount: { type: 'amount', value: 89500, currencyId: 2, label: null } }, 1, [usd, lbp])
    expect(input.discountAmount).toBe(1)
    expect(input.discountPercentage).toBeUndefined()
  })
})
