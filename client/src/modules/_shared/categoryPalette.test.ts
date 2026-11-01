import { describe, expect, it } from 'vitest'
import { CATEGORY_COLORS, categoryColor, categoryTone } from './categoryPalette'

/** Reference implementation with BigInt: the C# `unchecked(hash * 31 + ch)` followed by `(uint)hash % 7`, without relying on Math.imul. */
function reference(name: string): number {
  let hash = 17n
  for (const ch of name.trim().toLowerCase()) hash = BigInt.asUintN(32, hash * 31n + BigInt(ch.charCodeAt(0)))
  return Number(hash % 7n)
}

describe('categoryPalette', () => {
  it('matches the 32-bit C# hash for plain, long and Arabic names', () => {
    const names = ['Drinks', 'Dairy', 'Snacks', 'Cleaning', 'Fruits & Vegetables', 'مشروبات', 'ألبان', 'A very long category name that overflows a 32 bit hash many times over', '']
    for (const name of names) expect(categoryTone(name)).toBe(reference(name))
  })

  it('ignores case and surrounding spaces, like the C# key', () => {
    expect(categoryTone('  DRINKS ')).toBe(categoryTone('drinks'))
  })

  it('treats a missing name as the empty name', () => {
    expect(categoryTone(null)).toBe(categoryTone(''))
    expect(categoryTone(undefined)).toBe(categoryTone(''))
  })

  it('always lands inside the palette', () => {
    for (const name of ['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h']) {
      expect(CATEGORY_COLORS).toContain(categoryColor(name))
    }
  })
})
