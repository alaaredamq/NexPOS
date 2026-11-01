import { describe, expect, it } from 'vitest'
import { moveItem, reconcileOrder, sameOrder } from './order'

describe('reconcileOrder', () => {
  it('follows the server until the user arranges something', () => {
    expect(reconcileOrder(null, [3, 1, 2])).toEqual([3, 1, 2])
  })

  it('keeps the arranged order', () => {
    expect(reconcileOrder([2, 3, 1], [1, 2, 3])).toEqual([2, 3, 1])
  })

  it('drops rows that disappeared and appends rows that appeared', () => {
    expect(reconcileOrder([2, 9, 1], [1, 2, 4])).toEqual([2, 1, 4])
  })
})

describe('sameOrder', () => {
  it('compares position by position', () => {
    expect(sameOrder([1, 2], [1, 2])).toBe(true)
    expect(sameOrder([1, 2], [2, 1])).toBe(false)
    expect(sameOrder([1], [1, 2])).toBe(false)
  })
})

describe('moveItem', () => {
  it('moves an item down and up', () => {
    expect(moveItem(['a', 'b', 'c'], 0, 2)).toEqual(['b', 'c', 'a'])
    expect(moveItem(['a', 'b', 'c'], 2, 0)).toEqual(['c', 'a', 'b'])
  })

  it('leaves the list alone for out-of-range or no-op moves, and never mutates the input', () => {
    const input = ['a', 'b']
    expect(moveItem(input, 0, 5)).toEqual(['a', 'b'])
    expect(moveItem(input, -1, 0)).toEqual(['a', 'b'])
    expect(moveItem(input, 1, 1)).toEqual(['a', 'b'])
    expect(moveItem(input, 0, 1)).not.toBe(input)
    expect(input).toEqual(['a', 'b'])
  })
})
