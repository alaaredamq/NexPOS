/**
 * Weight / volume units of the till — a port of `UnitConverter.js` (track P3).
 *
 * A product sold by weight or volume has a unit (`kg`, `liter` …). The cashier may type the amount in any unit of the same
 * family (300 g), and the line is sent to the server in the PRODUCT's unit (0.3 kg) because the unit price is per product unit.
 * Counting units (`piece`, `box`, `pack`) never convert.
 *
 * One fix over the old file: the long names (`gram`, `litre` …) are now aliases of the short ones. The old converter looked the
 * long name up in the multiplier table, missed, and silently used 1 (so "gram" behaved like "kg").
 */

export type UnitFamily = 'weight' | 'volume' | 'count'

/** Multipliers to the family's base unit (kg / litre). */
const WEIGHT: Record<string, number> = { kg: 1, g: 0.001, mg: 0.000001, lb: 0.453592, oz: 0.0283495 }
const VOLUME: Record<string, number> = { liter: 1, ml: 0.001, gallon: 3.78541, floz: 0.0295735 }

const ALIASES: Record<string, string> = {
  kilogram: 'kg',
  gram: 'g',
  milligram: 'mg',
  pound: 'lb',
  ounce: 'oz',
  l: 'liter',
  litre: 'liter',
  milliliter: 'ml',
  millilitre: 'ml',
}

/** `"Kg"` → `kg`, `"litre"` → `liter`. Empty / unknown input is returned lower-cased. */
export function normalizeUnit(unit: string | null | undefined): string {
  const key = (unit ?? '').trim().toLowerCase()
  return ALIASES[key] ?? key
}

export function unitFamily(unit: string | null | undefined): UnitFamily {
  const key = normalizeUnit(unit)
  if (key in WEIGHT) return 'weight'
  if (key in VOLUME) return 'volume'
  return 'count'
}

/** The units a cashier may type an amount in for a product sold in `productUnit`. Counting products have none to choose. */
export function availableUnits(productUnit: string | null | undefined): string[] {
  const family = unitFamily(productUnit)
  if (family === 'weight') return Object.keys(WEIGHT)
  if (family === 'volume') return Object.keys(VOLUME)
  return []
}

/** Float noise (0.1 + 0.2) and absurd precision never reach the server: 6 decimals is finer than a milligram. */
export function cleanAmount(value: number): number {
  return Math.round(value * 1e6) / 1e6
}

/**
 * Converts between two units of the same family. Units of different families (or counting units) are returned unchanged — the
 * old file warned and did the same.
 */
export function convertUnit(amount: number, from: string | null | undefined, to: string | null | undefined): number {
  const a = normalizeUnit(from)
  const b = normalizeUnit(to)
  if (!a || !b || a === b) return amount
  const family = unitFamily(a)
  if (family === 'count' || family !== unitFamily(b)) return amount
  const table = family === 'weight' ? WEIGHT : VOLUME
  return cleanAmount((amount * table[a]!) / table[b]!)
}

/** Short label shown in the cart: `kg`, `g`, `L`, `mL`, `gal`, `fl oz`; counting units as stored. */
export function formatUnit(unit: string | null | undefined): string {
  const key = normalizeUnit(unit)
  switch (key) {
    case '':
      return 'piece'
    case 'liter':
      return 'L'
    case 'ml':
      return 'mL'
    case 'gallon':
      return 'gal'
    case 'floz':
      return 'fl oz'
    default:
      return key
  }
}

/** The unit the amount dialog starts on: the small unit (g / mL) because that is what a counter scale shows. */
export function defaultEntryUnit(productUnit: string | null | undefined): string {
  const key = normalizeUnit(productUnit)
  if (unitFamily(key) === 'weight') return 'g'
  if (unitFamily(key) === 'volume') return 'ml'
  return key
}
