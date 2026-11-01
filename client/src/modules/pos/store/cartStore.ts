import { create } from 'zustand'
import type { components } from '@/api/schema'
import { cleanAmount, convertUnit, normalizeUnit, unitFamily } from '../cart/units'

/**
 * The till's cart (track P3). Plain client state: what the cashier has put on the counter. It never holds a figure the server
 * decides — unit prices in the cart currency, line totals and the total come from `useCartTotals()` (the debounced `pos/price`
 * call), and a sale is made from `buildCartInput()` + that call's total. See `docs/react-migration/notes/P3.md`.
 *
 * Public interface for the other POS tracks:
 *  - P2 (catalog / scan):  `useAddToCart()` from `../cart/useAddToCart` (asks for the weight first when needed), or `addProduct` directly;
 *  - P4 (checkout):        `useCartTotals()` (`canCheckout`, `total`, `isServer`), `buildCartInput()`, `setCustomerId`, `clear`.
 */

type Schemas = components['schemas']
export type PosProduct = Schemas['PosProductDto']
export type PosCurrency = Schemas['PosCurrencyDto']
export type PosPricedCart = Schemas['PosPricedCartDto']
export type PosPricedLine = Schemas['PosPricedLineDto']
export type PosCartInput = Schemas['PosCartInput']

export type UnitKind = 'count' | 'weight' | 'volume'

/**
 * How a line is priced. `amount` is a unit price in `currencyId` (the currency it was chosen or parked in); it is converted to
 * the cart currency only when it is sent — the original is kept so switching the cart currency back and forth loses nothing.
 * The server re-checks every one of them (a tier must be the product's, a custom price may not undercut the purchase price).
 */
export type LinePrice =
  | { kind: 'default' }
  | { kind: 'tier'; tierId: number; label: string; amount: number; currencyId: number }
  | { kind: 'custom'; amount: number; currencyId: number }

export interface CartLine {
  productId: number
  name: string
  barcode: string
  categoryId: number
  /** The product's own currency and default unit price in it. */
  currencyId: number
  sellingPrice: number
  /** The product's unit as stored (`kg`, `liter`, `piece` …), null = pieces. */
  unit: string | null
  unitKind: UnitKind
  /** False for packs: no stored extra prices to choose from. */
  canChangePrice: boolean
  /** On hand when the line was added; the live figure comes with the server's price. */
  stockQuantity: number
  /** Whole pieces (1 for a weight / volume line). */
  quantity: number
  /** Amount in the product's unit (0.3 for 300 g of a kg product); null for pieces. */
  weightOrVolume: number | null
  /** What the cashier typed (300 g) — only for display. */
  displayValue: number | null
  displayUnit: string | null
  price: LinePrice
}

/** Money off the whole cart. An `amount` is in `currencyId` like a line price. */
export type CartDiscount =
  | { type: 'percentage'; value: number; label: string | null }
  | { type: 'amount'; value: number; currencyId: number; label: string | null }

export interface AddEntry {
  /** Pieces to add (default 1). */
  quantity?: number
  /** Amount in the PRODUCT's unit, for a weight / volume product. */
  weightOrVolume?: number
  displayValue?: number
  displayUnit?: string
  /** Replaces the product's name on the line ("Cola (from 6-pack)"). */
  name?: string
}

/** The part of the state that is "the sale in progress": what undo restores and what hold / resume carry. */
export interface CartContent {
  lines: CartLine[]
  discount: CartDiscount | null
  customerId: number | null
  notes: string
}

export interface CartState extends CartContent {
  /** The cart currency; null until the currency list has loaded (`useCartCurrency` picks the default). */
  currencyId: number | null
  /** The held cart this one was resumed from (it stays in the held list until deleted, as in the old till). */
  heldCartId: number | null
  /** What `undo()` brings back: the cart as it was before the last removal / clear. Newest last. */
  undoStack: CartContent[]
}

export interface CartActions {
  /** Puts a product on the cart. One line per product: a repeat adds to the piece count / weight of the existing line. */
  addProduct: (product: PosProduct, entry?: AddEntry) => void
  /** Pieces of a counted line. 0 or less removes the line (undo-able). Ignored for weight / volume lines. */
  setQty: (productId: number, quantity: number) => void
  /** New weight / volume of a line: `value` typed in `unit`, stored in the product's unit. */
  setWeight: (productId: number, value: number, unit: string) => void
  /** Shows the same weight / volume in another unit of its family (300 g → 0.3 kg); the amount sold does not change. */
  setUnit: (productId: number, unit: string) => void
  setPrice: (productId: number, price: LinePrice) => void
  remove: (productId: number) => void
  /** Voids the most recently added line. */
  removeLast: () => void
  /** Brings back what the last `remove` / `removeLast` / `clear` took away. */
  undo: () => void
  /** Whole-cart discount; null removes it. */
  applyDiscount: (discount: CartDiscount | null) => void
  clear: () => void
  setCurrency: (currencyId: number) => void
  setCustomerId: (customerId: number | null) => void
  setNotes: (notes: string) => void
  /** Replaces the cart (resume a held cart). */
  load: (content: Partial<CartContent>, heldCartId?: number | null) => void
}

export type CartStore = CartState & CartActions

const UNDO_DEPTH = 10

const emptyContent = (): CartContent => ({ lines: [], discount: null, customerId: null, notes: '' })

export const initialCartState = (): CartState => ({
  ...emptyContent(),
  currencyId: null,
  heldCartId: null,
  undoStack: [],
})

function unitKindOf(product: PosProduct): UnitKind {
  return product.unitKind === 'weight' || product.unitKind === 'volume' ? product.unitKind : 'count'
}

/** A new line for `product`. Exported for resumed carts, which rebuild lines from a product fetch. */
export function lineFromProduct(product: PosProduct, entry: AddEntry = {}): CartLine {
  const kind = unitKindOf(product)
  const byAmount = kind !== 'count' && entry.weightOrVolume !== undefined
  return {
    productId: product.id,
    name: entry.name ?? product.name,
    barcode: product.barcode,
    categoryId: product.categoryId,
    currencyId: product.currencyId,
    sellingPrice: product.sellingPrice,
    unit: product.unit ?? null,
    unitKind: kind,
    canChangePrice: product.canChangePrice,
    stockQuantity: product.stockQuantity,
    quantity: byAmount ? 1 : Math.max(1, Math.floor(entry.quantity ?? 1)),
    weightOrVolume: byAmount ? cleanAmount(entry.weightOrVolume!) : null,
    displayValue: byAmount ? (entry.displayValue ?? cleanAmount(entry.weightOrVolume!)) : null,
    displayUnit: byAmount ? normalizeUnit(entry.displayUnit ?? product.unit) : null,
    price: { kind: 'default' },
  }
}

/** Same product added again: pieces add up; weights add up and the display follows the unit when both were typed in the same one. */
function merge(existing: CartLine, line: CartLine): CartLine {
  if (line.weightOrVolume !== null) {
    const weightOrVolume = cleanAmount((existing.weightOrVolume ?? 0) + line.weightOrVolume)
    const sameUnit = existing.displayUnit === line.displayUnit && existing.displayValue !== null && line.displayValue !== null
    return {
      ...existing,
      weightOrVolume,
      displayValue: sameUnit ? cleanAmount(existing.displayValue! + line.displayValue!) : weightOrVolume,
      displayUnit: sameUnit ? existing.displayUnit : normalizeUnit(existing.unit),
    }
  }
  return { ...existing, quantity: existing.quantity + line.quantity }
}

const MAX_QUANTITY = 100000

/** Pushes the content before a destructive change so `undo()` can bring it back. Empty carts are not worth restoring. */
function remember(state: CartState): CartContent[] {
  if (state.lines.length === 0) return state.undoStack
  const snapshot: CartContent = { lines: state.lines, discount: state.discount, customerId: state.customerId, notes: state.notes }
  return [...state.undoStack, snapshot].slice(-UNDO_DEPTH)
}

const mapLine = (lines: CartLine[], productId: number, change: (line: CartLine) => CartLine) =>
  lines.map((line) => (line.productId === productId ? change(line) : line))

export const useCartStore = create<CartStore>()((set) => ({
  ...initialCartState(),

  addProduct: (product, entry) =>
    set((state) => {
      const line = lineFromProduct(product, entry)
      const at = state.lines.findIndex((l) => l.productId === line.productId)
      if (at < 0) return { lines: [...state.lines, line] }
      const lines = [...state.lines]
      lines[at] = merge(lines[at]!, line)
      return { lines }
    }),

  setQty: (productId, quantity) =>
    set((state) => {
      const line = state.lines.find((l) => l.productId === productId)
      if (!line || line.weightOrVolume !== null) return state
      if (!(quantity >= 1)) {
        return { lines: state.lines.filter((l) => l.productId !== productId), undoStack: remember(state) }
      }
      const next = Math.min(MAX_QUANTITY, Math.floor(quantity))
      return { lines: mapLine(state.lines, productId, (l) => ({ ...l, quantity: next })) }
    }),

  setWeight: (productId, value, unit) =>
    set((state) => {
      const line = state.lines.find((l) => l.productId === productId)
      if (!line || line.unitKind === 'count' || !(value > 0)) return state
      const displayUnit = normalizeUnit(unit)
      // A unit of the wrong family (a volume for a weight product) would be sent unconverted: keep the product's own unit instead.
      const usable = unitFamily(displayUnit) === unitFamily(line.unit)
      const weightOrVolume = cleanAmount(usable ? convertUnit(value, displayUnit, line.unit) : value)
      return {
        lines: mapLine(state.lines, productId, (l) => ({
          ...l,
          quantity: 1,
          weightOrVolume,
          displayValue: usable ? cleanAmount(value) : weightOrVolume,
          displayUnit: usable ? displayUnit : normalizeUnit(l.unit),
        })),
      }
    }),

  setUnit: (productId, unit) =>
    set((state) => {
      const line = state.lines.find((l) => l.productId === productId)
      if (!line || line.weightOrVolume === null || unitFamily(unit) !== unitFamily(line.unit)) return state
      const displayUnit = normalizeUnit(unit)
      return {
        lines: mapLine(state.lines, productId, (l) => ({
          ...l,
          displayUnit,
          displayValue: convertUnit(l.weightOrVolume!, l.unit, displayUnit),
        })),
      }
    }),

  setPrice: (productId, price) => set((state) => ({ lines: mapLine(state.lines, productId, (l) => ({ ...l, price })) })),

  remove: (productId) =>
    set((state) =>
      state.lines.some((l) => l.productId === productId)
        ? { lines: state.lines.filter((l) => l.productId !== productId), undoStack: remember(state) }
        : state,
    ),

  removeLast: () =>
    set((state) => (state.lines.length === 0 ? state : { lines: state.lines.slice(0, -1), undoStack: remember(state) })),

  undo: () =>
    set((state) => {
      const previous = state.undoStack.at(-1)
      if (!previous) return state
      return { ...previous, undoStack: state.undoStack.slice(0, -1) }
    }),

  applyDiscount: (discount) => set({ discount }),

  clear: () =>
    set((state) => ({ ...emptyContent(), heldCartId: null, undoStack: remember(state) })),

  setCurrency: (currencyId) => set({ currencyId }),

  setCustomerId: (customerId) => set({ customerId }),

  setNotes: (notes) => set({ notes }),

  load: (content, heldCartId = null) =>
    set((state) => ({ ...emptyContent(), ...content, heldCartId, undoStack: remember(state) })),
}))

/**
 * Empties the cart AND its undo history — what P4 calls after a completed sale (so undo cannot resurrect a sold cart), and on sign-out.
 * The cart currency is kept unless one is given. The cashier's own "clear" is `clear()`, which can be undone.
 */
export function resetCart(currencyId: number | null = useCartStore.getState().currencyId): void {
  useCartStore.setState({ ...initialCartState(), currencyId })
}

export const selectItemCount = (state: CartContent) => state.lines.length

export const selectIsEmpty = (state: CartContent) => state.lines.length === 0
