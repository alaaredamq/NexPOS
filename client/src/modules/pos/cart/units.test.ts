import { describe, expect, it } from 'vitest'
import { availableUnits, cleanAmount, convertUnit, defaultEntryUnit, formatUnit, normalizeUnit, unitFamily } from './units'

describe('units', () => {
  it('classifies units and their long names', () => {
    expect(unitFamily('kg')).toBe('weight')
    expect(unitFamily('Gram')).toBe('weight')
    expect(unitFamily('litre')).toBe('volume')
    expect(unitFamily('floz')).toBe('volume')
    expect(unitFamily('piece')).toBe('count')
    expect(unitFamily(null)).toBe('count')
  })

  it('converts inside a family with the multipliers of the old UnitConverter', () => {
    expect(convertUnit(300, 'g', 'kg')).toBe(0.3)
    expect(convertUnit(1, 'kg', 'g')).toBe(1000)
    expect(convertUnit(500, 'ml', 'liter')).toBe(0.5)
    expect(convertUnit(1, 'lb', 'kg')).toBeCloseTo(0.453592, 9)
    expect(convertUnit(2, 'kg', 'kg')).toBe(2)
  })

  it('treats the long names as aliases (the old converter used a multiplier of 1 for "gram")', () => {
    expect(convertUnit(300, 'gram', 'kilogram')).toBe(0.3)
    expect(convertUnit(1, 'l', 'ml')).toBe(1000)
  })

  it('leaves amounts alone across families and for counting units', () => {
    expect(convertUnit(5, 'kg', 'liter')).toBe(5)
    expect(convertUnit(5, 'box', 'piece')).toBe(5)
  })

  it('lists what a cashier can type an amount in', () => {
    expect(availableUnits('kg')).toEqual(['kg', 'g', 'mg', 'lb', 'oz'])
    expect(availableUnits('liter')).toEqual(['liter', 'ml', 'gallon', 'floz'])
    expect(availableUnits('piece')).toEqual([])
    expect(defaultEntryUnit('kg')).toBe('g')
    expect(defaultEntryUnit('liter')).toBe('ml')
  })

  it('labels units like the old cart', () => {
    expect(formatUnit('liter')).toBe('L')
    expect(formatUnit('ml')).toBe('mL')
    expect(formatUnit('floz')).toBe('fl oz')
    expect(formatUnit(null)).toBe('piece')
    expect(normalizeUnit(' KG ')).toBe('kg')
  })

  it('strips float noise', () => {
    expect(cleanAmount(0.1 + 0.2)).toBe(0.3)
  })
})
