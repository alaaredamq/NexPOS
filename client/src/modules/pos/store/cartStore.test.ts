import { beforeEach, describe, expect, it } from 'vitest'
import { resetCart, useCartStore, type PosProduct } from './cartStore'

const product = (id: number, overrides: Partial<PosProduct> = {}): PosProduct => ({
  id,
  name: `Product ${id}`,
  barcode: `000${id}`,
  categoryId: 1,
  categoryName: 'Drinks',
  currencyId: 1,
  currencyNotation: '$',
  sellingPrice: 2,
  supplierDiscountRate: 0,
  stockQuantity: 10,
  unit: null,
  unitKind: 'count',
  isPack: false,
  canChangePrice: true,
  ...overrides,
})

const kg = (id: number) => product(id, { unit: 'kg', unitKind: 'weight', sellingPrice: 11.5 })

const cart = () => useCartStore.getState()

beforeEach(() => resetCart(1))

describe('adding', () => {
  it('keeps one line per product and adds the pieces up', () => {
    cart().addProduct(product(1))
    cart().addProduct(product(1), { quantity: 3 })
    cart().addProduct(product(2))
    expect(cart().lines.map((l) => [l.productId, l.quantity])).toEqual([
      [1, 4],
      [2, 1],
    ])
  })

  it('adds weights up and keeps the typed unit while it stays the same', () => {
    cart().addProduct(kg(5), { weightOrVolume: 0.3, displayValue: 300, displayUnit: 'g' })
    cart().addProduct(kg(5), { weightOrVolume: 0.2, displayValue: 200, displayUnit: 'g' })
    expect(cart().lines[0]).toMatchObject({ quantity: 1, weightOrVolume: 0.5, displayValue: 500, displayUnit: 'g' })
  })

  it('falls back to the product unit when two different units meet', () => {
    cart().addProduct(kg(5), { weightOrVolume: 0.3, displayValue: 300, displayUnit: 'g' })
    cart().addProduct(kg(5), { weightOrVolume: 1, displayValue: 1, displayUnit: 'kg' })
    expect(cart().lines[0]).toMatchObject({ weightOrVolume: 1.3, displayValue: 1.3, displayUnit: 'kg' })
  })

  it('ignores a weight for a product sold by the piece', () => {
    cart().addProduct(product(1), { weightOrVolume: 2 })
    expect(cart().lines[0]).toMatchObject({ quantity: 1, weightOrVolume: null })
  })
})

describe('quantity and units', () => {
  it('sets pieces, and a quantity below 1 removes the line', () => {
    cart().addProduct(product(1))
    cart().setQty(1, 5)
    expect(cart().lines[0]?.quantity).toBe(5)
    cart().setQty(1, 0)
    expect(cart().lines).toHaveLength(0)
  })

  it('does not change the pieces of a weighed line', () => {
    cart().addProduct(kg(5), { weightOrVolume: 0.3, displayValue: 300, displayUnit: 'g' })
    cart().setQty(5, 4)
    expect(cart().lines[0]?.quantity).toBe(1)
  })

  it('stores a typed weight in the product unit', () => {
    cart().addProduct(kg(5), { weightOrVolume: 1, displayValue: 1, displayUnit: 'kg' })
    cart().setWeight(5, 750, 'g')
    expect(cart().lines[0]).toMatchObject({ weightOrVolume: 0.75, displayValue: 750, displayUnit: 'g' })
  })

  it('shows the same weight in another unit without selling more', () => {
    cart().addProduct(kg(5), { weightOrVolume: 0.3, displayValue: 300, displayUnit: 'g' })
    cart().setUnit(5, 'kg')
    expect(cart().lines[0]).toMatchObject({ weightOrVolume: 0.3, displayValue: 0.3, displayUnit: 'kg' })
    cart().setUnit(5, 'ml') // wrong family: ignored
    expect(cart().lines[0]?.displayUnit).toBe('kg')
  })
})

describe('undo', () => {
  it('brings back a removed line, then the earlier ones', () => {
    cart().addProduct(product(1))
    cart().addProduct(product(2))
    cart().remove(2)
    cart().removeLast()
    expect(cart().lines).toHaveLength(0)
    cart().undo()
    expect(cart().lines.map((l) => l.productId)).toEqual([1])
    cart().undo()
    expect(cart().lines.map((l) => l.productId)).toEqual([1, 2])
    cart().undo()
    expect(cart().lines).toHaveLength(2)
  })

  it('brings back a cleared cart with its discount', () => {
    cart().addProduct(product(1))
    cart().applyDiscount({ type: 'percentage', value: 10, label: null })
    cart().clear()
    expect(cart().lines).toHaveLength(0)
    expect(cart().discount).toBeNull()
    cart().undo()
    expect(cart().lines).toHaveLength(1)
    expect(cart().discount).toMatchObject({ value: 10 })
  })

  it('has nothing to restore after a finished sale', () => {
    cart().addProduct(product(1))
    resetCart()
    cart().undo()
    expect(cart().lines).toHaveLength(0)
    expect(cart().currencyId).toBe(1)
  })
})

describe('prices and held carts', () => {
  it('remembers the price choice of a line', () => {
    cart().addProduct(product(1))
    cart().setPrice(1, { kind: 'custom', amount: 1.5, currencyId: 1 })
    expect(cart().lines[0]?.price).toEqual({ kind: 'custom', amount: 1.5, currencyId: 1 })
  })

  it('loads a held cart, remembers where it came from, and keeps the replaced cart recoverable', () => {
    cart().addProduct(product(1))
    cart().load({ lines: [], customerId: 4, notes: 'back at 5' }, 12)
    expect(cart()).toMatchObject({ lines: [], customerId: 4, notes: 'back at 5', heldCartId: 12 })
    cart().undo()
    expect(cart().lines).toHaveLength(1)
  })
})
