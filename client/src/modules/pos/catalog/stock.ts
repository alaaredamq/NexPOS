import type { PosProduct } from './api'

export type StockLevel = 'out' | 'low' | 'ok'

/** The old tile's default when a product has no minimum of its own. */
export const DEFAULT_MINIMUM_STOCK = 5

/** Same rule as the Razor tile: nothing left, then at or below the minimum (5 when unset), else fine. */
export function stockLevel(product: Pick<PosProduct, 'stockQuantity' | 'minimumStock'>): StockLevel {
  if (product.stockQuantity <= 0) return 'out'
  return product.stockQuantity <= (product.minimumStock ?? DEFAULT_MINIMUM_STOCK) ? 'low' : 'ok'
}
