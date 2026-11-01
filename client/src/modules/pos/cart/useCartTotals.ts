import { useCallback, useEffect, useMemo } from 'react'
import type { ApiError } from '@/api/http'
import { useDebouncedValue } from '@/ui/hooks/useDebouncedValue'
import { useCartStore, type PosCartInput, type PosCurrency, type PosPricedCart } from '../store/cartStore'
import { useCurrencies, usePricedCart } from './api'
import { buildCartInput, cartInputKey, previewCart, totalInCurrency } from './pricing'

/** How long the cart must stay unchanged before it is sent to `pos/price`. */
export const PRICE_DEBOUNCE_MS = 250

const NO_CURRENCIES: PosCurrency[] = []

/** What the cart shows for one line: the server's figures once they are in, the local preview until then. */
export interface LineView {
  unitPrice: number
  lineTotal: number
  /** Lowest custom unit price (cart currency). Only known from the server. */
  minUnitPrice: number | null
  /** The line would take stock below zero (checkout will ask "sell anyway"). */
  insufficientStock: boolean
  /** On hand now, from the server; null until it answers. */
  stockQuantity: number | null
}

export type TotalsStatus =
  /** Nothing in the cart. */
  | 'empty'
  /** The currency list has not loaded yet. */
  | 'waiting'
  /** The cart changed; the numbers are the local preview until the server answers. */
  | 'pricing'
  /** The numbers are the server's, for exactly this cart. */
  | 'ready'
  /** The server refused to price this cart (`error.code`: `price_below_minimum`, `product_not_found` …) or could not be reached. */
  | 'error'

export interface CartTotals {
  status: TotalsStatus
  /** True only when `status` is `ready`: every figure below is the server's for the cart as it is now. */
  isServer: boolean
  /** The sale can be made from this cart: it is priced by the server and nothing is pending or refused. P4's checkout button hangs on this. */
  canCheckout: boolean
  currencyId: number | null
  notation: string
  currencies: PosCurrency[]
  lines: Map<number, LineView>
  subtotal: number
  discountAmount: number
  discountPercentage: number
  taxAmount: number
  /** What the customer pays. Send it as `totalAmount` at checkout (only when `isServer`). */
  total: number
  hasInsufficientStock: boolean
  error: ApiError | null
  /** The cart exactly as it was priced (what to send to checkout); null while the cart is empty. */
  input: PosCartInput | null
  /** The server's document when `isServer`. */
  priced: PosPricedCart | null
  /** The total in every currency (display only): `{ [currencyId]: amount }`. */
  totalsByCurrency: Record<number, number>
  retry: () => void
}

/**
 * The cart's money, as the screen should show it. Every cart change is debounced and sent to `pos/price`; while the answer is on
 * its way the local preview (`previewCart`) keeps the screen readable and `isServer` is false. Any number of components can call
 * this — they share one query (same key), so the server is asked once.
 */
export function useCartTotals(): CartTotals {
  const lines = useCartStore((s) => s.lines)
  const discount = useCartStore((s) => s.discount)
  const currencyId = useCartStore((s) => s.currencyId)
  const { data: currencyList } = useCurrencies()
  const currencies = currencyList ?? NO_CURRENCIES

  const input = useMemo(
    () => (currencyId !== null && lines.length > 0 && currencies.length > 0 ? buildCartInput({ lines, discount }, currencyId, currencies) : null),
    [lines, discount, currencyId, currencies],
  )
  const key = useMemo(() => (input ? cartInputKey(input) : ''), [input])

  const pending = useMemo(() => ({ input, key }), [input, key]) // memoised: a new object per render would re-arm the debounce forever
  const settled = useDebouncedValue(pending, PRICE_DEBOUNCE_MS)
  const query = usePricedCart(settled.input, settled.key)

  const current = input !== null && settled.key === key
  const failed = current && query.isError ? (query.error as ApiError) : null
  const server = current && !failed && !query.isPlaceholderData && query.data ? query.data : null

  const preview = useMemo(
    () => (currencyId !== null ? previewCart({ lines, discount }, currencyId, currencies) : null),
    [lines, discount, currencyId, currencies],
  )

  const notation = server?.currencyNotation ?? currencies.find((c) => c.id === currencyId)?.notation ?? ''

  const lineViews = useMemo(() => {
    const views = new Map<number, LineView>()
    if (server) {
      for (const l of server.lines) {
        views.set(l.productId, {
          unitPrice: l.unitPrice,
          lineTotal: l.lineTotal,
          minUnitPrice: l.minUnitPrice,
          insufficientStock: l.insufficientStock,
          stockQuantity: l.stockQuantity,
        })
      }
    } else if (preview) {
      for (const [productId, l] of preview.lines) {
        views.set(productId, { ...l, minUnitPrice: null, insufficientStock: false, stockQuantity: null })
      }
    }
    return views
  }, [server, preview])

  const status: TotalsStatus =
    lines.length === 0 ? 'empty' : input === null ? 'waiting' : failed ? 'error' : server ? 'ready' : 'pricing'

  const subtotal = server?.subtotal ?? preview?.subtotal ?? 0
  const total = server?.total ?? preview?.total ?? 0
  const priced = server

  const totalsByCurrency = useMemo(() => {
    const byCurrency: Record<number, number> = {}
    if (currencyId === null) return byCurrency
    for (const c of currencies) byCurrency[c.id] = totalInCurrency({ total, currencyId }, c.id, currencies)
    return byCurrency
  }, [total, currencyId, currencies])

  // Re-ask: the cashier fixes the cause of a refusal (removes the line) and the key changes by itself; this is for "could not reach the server".
  const refetch = query.refetch
  const retry = useCallback(() => void refetch(), [refetch])

  return {
    status,
    isServer: status === 'ready',
    canCheckout: status === 'ready',
    currencyId,
    notation,
    currencies,
    lines: lineViews,
    subtotal,
    discountAmount: server?.discountAmount ?? preview?.discountAmount ?? 0,
    discountPercentage: server?.discountPercentage ?? preview?.discountPercentage ?? 0,
    taxAmount: server?.taxAmount ?? 0,
    total,
    hasInsufficientStock: server?.hasInsufficientStock ?? false,
    error: failed,
    input,
    priced,
    totalsByCurrency,
    retry,
  }
}

/**
 * The cart currency: the store's choice, or the shop's default currency until the cashier picks one. Mount it once (CartPanel does).
 */
export function useCartCurrency() {
  const currencyId = useCartStore((s) => s.currencyId)
  const setCurrency = useCartStore((s) => s.setCurrency)
  const { data } = useCurrencies()
  const currencies = data ?? NO_CURRENCIES

  useEffect(() => {
    if (currencyId !== null || currencies.length === 0) return
    const initial = currencies.find((c) => c.isDefault) ?? currencies[0]
    if (initial) setCurrency(initial.id)
  }, [currencyId, currencies, setCurrency])

  const currency = currencies.find((c) => c.id === currencyId) ?? null
  return { currency, currencies, setCurrency }
}
