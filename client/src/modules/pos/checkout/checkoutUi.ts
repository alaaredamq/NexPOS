import { create } from 'zustand'
import type { PosCartInput, PosCurrency } from '../store/cartStore'
import type { PosSale } from './api'

/**
 * The cart exactly as the server priced it when the cashier pressed Checkout. The dialog sells *this*, not the live cart: the figures the
 * cashier confirms and the figures sent are one and the same thing even if something behind the dialog moves.
 */
export interface CheckoutSnapshot {
  input: PosCartInput
  currencyId: number
  notation: string
  subtotal: number
  discountAmount: number
  /** The server's total: what the dialog proposes and what `totalAmount` is checked against. */
  total: number
  hasInsufficientStock: boolean
  currencies: PosCurrency[]
  /** The cart in every currency (display only). */
  totalsByCurrency: Record<number, number>
  lineCount: number
}

/** What the success dialog shows besides the server's sale: cash handed over (the server does not know it; it is for the change line). */
export interface FinishedSale {
  sale: PosSale
  received: number | null
  change: number
}

export type CheckoutDialog = { kind: 'pay'; snapshot: CheckoutSnapshot } | { kind: 'done'; finished: FinishedSale } | { kind: 'recent' }

interface CheckoutUiState {
  dialog: CheckoutDialog | null
  open: (dialog: CheckoutDialog) => void
  close: () => void
}

/** Which post-cart dialog is open. A store (not component state) so the slot's commands and the dialogs' own buttons all reach it. */
export const useCheckoutUi = create<CheckoutUiState>()((set) => ({
  dialog: null,
  open: (dialog) => set({ dialog }),
  close: () => set({ dialog: null }),
}))
