import { create } from 'zustand'
import { formatMoney, formatPercent } from '@/ui/format'
import type { AddEntry, PosProduct } from '../store/cartStore'

/** Cart money as the till prints it: whole amounts of 1,000 or more drop the `.00` (LBP 859,200); everything else keeps two decimals. */
export function money(amount: number, notation: string): string {
  return formatMoney(amount, notation, Math.abs(amount) >= 1000 && Number.isInteger(amount) ? 0 : 2)
}

export { formatPercent }

/** What the amount dialog is asked for: a weight / volume for a product, or a new one for a line already in the cart. */
export interface AmountRequest {
  product: Pick<PosProduct, 'id' | 'name' | 'unit'>
  /** Unit price in the cart currency, per product unit. */
  unitPrice: number
  notation: string
  /** Editing a line that already holds an amount. */
  initial?: { displayValue: number; displayUnit: string }
  resolve: (entry: Pick<AddEntry, 'weightOrVolume' | 'displayValue' | 'displayUnit'> | null) => void
}

export type CartDialog =
  | { kind: 'discount' }
  | { kind: 'hold' }
  | { kind: 'held' }
  | { kind: 'price'; productId: number }

interface CartUiState {
  dialog: CartDialog | null
  amountRequest: AmountRequest | null
  open: (dialog: CartDialog) => void
  close: () => void
}

/**
 * Which cart dialog is open. A store (not component state) so other parts of the screen can open them — P2's keyboard shortcuts
 * call `useCartUi.getState().open({ kind: 'held' })` — and so `addToCart()` can ask for a weight from outside React.
 * The dialogs themselves render inside `<CartPanel />`.
 */
export const useCartUi = create<CartUiState>()((set) => ({
  dialog: null,
  amountRequest: null,
  open: (dialog) => set({ dialog }),
  close: () => set({ dialog: null }),
}))

/** Asks the cashier for a weight / volume. Resolves null when they cancel. A second request cancels the first. */
export function requestAmount(request: Omit<AmountRequest, 'resolve'>): Promise<Parameters<AmountRequest['resolve']>[0]> {
  return new Promise((resolve) => {
    useCartUi.getState().amountRequest?.resolve(null)
    useCartUi.setState({ amountRequest: { ...request, resolve } })
  })
}

export function settleAmount(entry: Parameters<AmountRequest['resolve']>[0]): void {
  const request = useCartUi.getState().amountRequest
  useCartUi.setState({ amountRequest: null })
  request?.resolve(entry)
}
