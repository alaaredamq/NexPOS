/**
 * The contract between the POS screen (P2) and the cart (P3) and checkout (P4) tracks. This file is the only thing the three share
 * apart from the API DTOs: merge changes to it small and early, additive changes freely.
 *
 * ## How P3 and P4 plug in
 * - **P3** adds `modules/pos/cart/slot.tsx` with `export default function CartSlot() {...}`. It fills the cart column under the scan bar
 *   (P2 owns the scan bar and the column; the slot gets the rest of the column's height and must scroll inside itself).
 * - **P4** adds `modules/pos/checkout/slot.tsx` with `export default function CheckoutSlot() {...}`. It renders no layout of its own:
 *   it is mounted once at the root of the page and hosts the payment / customer / receipt dialogs.
 * - `layout/slotRegistry.ts` finds both files with `import.meta.glob`; until they exist P2 shows a stand-in, so nobody edits PosPage.
 *
 * ## What the catalog tells the slots (`posBus`)
 * - `addProduct`: a tile press, a scan or a search pick. The cart decides what that means: a product sold by weight or volume
 *   (`product.unitKind !== 'count'`) needs the cart to ask for the amount; an existing line gets its quantity raised. `quantity` is
 *   pieces and is already >= 1 (a "5*" prefix in the scan box becomes 5).
 * - `run(command)`: the quick-action buttons and keyboard shortcuts (`PosCommand`). P3 handles clear / hold / retrieve / manualItem /
 *   discount, P4 handles checkout. A command nobody handles is a no-op, so the buttons exist from day one.
 *
 * ## What the slots tell the catalog
 * - `usePosUi.setCartCount(n)`: P3 publishes the number of cart lines (shown on the narrow-screen "review cart" button).
 * - `posBus.focusScan()`: call it after a dialog closes so the next scan lands in the scan box. P2 also refocuses on its own
 *   when focus is lost and no dialog is open, so this is a courtesy, not a requirement.
 *
 * ## Scanning while a dialog is open
 * P2 stops capturing keys while any `[role="dialog"]` is in the document, so dialogs from P3/P4 should be built on `ui/Modal`.
 */
import { create } from 'zustand'
import type { components } from '@/api/schema'

export type PosProduct = components['schemas']['PosProductDto']

export interface AddProductRequest {
  product: PosProduct
  /** Whole pieces, at least 1. */
  quantity: number
  source: 'tile' | 'scan' | 'search'
}

export const POS_COMMANDS = ['checkout', 'hold', 'clear', 'retrieve', 'manualItem', 'discount'] as const
export type PosCommand = (typeof POS_COMMANDS)[number]

type Handler<T> = (payload: T) => void

/** A synchronous fan-out. `emit` reports whether anything was listening so the sender can tell "ignored" from "done". */
function channel<T>() {
  const handlers = new Set<Handler<T>>()
  return {
    on(handler: Handler<T>) {
      handlers.add(handler)
      return () => {
        handlers.delete(handler)
      }
    },
    emit(payload: T): boolean {
      for (const handler of [...handlers]) handler(payload)
      return handlers.size > 0
    },
  }
}

const addProduct = channel<AddProductRequest>()
const commands = new Map<PosCommand, ReturnType<typeof channel<void>>>(POS_COMMANDS.map((c) => [c, channel<void>()]))
const focusScan = channel<void>()

export const posBus = {
  /** True when a cart is listening. */
  addProduct: (request: AddProductRequest) => addProduct.emit(request),
  onAddProduct: (handler: Handler<AddProductRequest>) => addProduct.on(handler),
  /** True when someone handles the command. */
  run: (command: PosCommand) => commands.get(command)!.emit(),
  onCommand: (command: PosCommand, handler: Handler<void>) => commands.get(command)!.on(handler),
  focusScan: () => void focusScan.emit(),
  onFocusScan: (handler: Handler<void>) => focusScan.on(handler),
}

interface PosUiState {
  cartCount: number
  setCartCount: (count: number) => void
}

export const usePosUi = create<PosUiState>((set) => ({
  cartCount: 0,
  setCartCount: (cartCount) => set({ cartCount }),
}))
