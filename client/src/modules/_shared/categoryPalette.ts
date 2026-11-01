/**
 * One muted colour per product category, derived from the category's name so it never needs storing.
 * Port of `Common/Helpers/CategoryPalette.cs`: the POS tiles (P2) and the dashboard donut (I1) both import this, so a category is the
 * same colour on both screens. Keep the colours and the hash identical to the C# (golden cases are in categoryPalette.test.ts).
 * Red is left out on purpose: it already means "out of stock" / "danger".
 */
export const CATEGORY_COLORS = ['#0a7b85', '#2361d8', '#b4610f', '#178a50', '#a8456b', '#5a4ad1', '#5c6472'] as const

/** Index into {@link CATEGORY_COLORS}. Stable across runs: `hash = hash * 31 + char` over 32-bit ints, like the C# loop. */
export function categoryTone(categoryName: string | null | undefined): number {
  const key = (categoryName ?? '').trim().toLowerCase()
  let hash = 17
  for (let i = 0; i < key.length; i++) hash = (Math.imul(hash, 31) + key.charCodeAt(i)) | 0
  return (hash >>> 0) % CATEGORY_COLORS.length
}

export function categoryColor(categoryName: string | null | undefined): string {
  return CATEGORY_COLORS[categoryTone(categoryName)] ?? CATEGORY_COLORS[0]
}
