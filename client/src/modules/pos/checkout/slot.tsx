import { useCallback, useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { http } from '@/api/http'
import { useCan } from '@/core/permissions/permissions'
import { toast } from '@/ui/toast'
import { money } from '../cart/cartUi'
import { useCartTotals, type CartTotals } from '../cart/useCartTotals'
import { RecentSalesDialog } from '../receipt/RecentSalesDialog'
import { SaleCompleteDialog } from '../receipt/SaleCompleteDialog'
import { posBus } from '../slots'
import { resetCart, useCartStore } from '../store/cartStore'
import { useAfterSale, type PosSale } from './api'
import { useCheckoutUi, type CheckoutSnapshot } from './checkoutUi'
import { PaymentDialog } from './PaymentDialog'
import { getShowSaleComplete } from './saleCompletePref'
import { forgetAttempt } from './tender'

/** What the dialog sells: the cart as the server priced it (`totals.input`) with the figures the cashier saw. Null while there is nothing safe to sell. */
function snapshotOf(totals: CartTotals): CheckoutSnapshot | null {
  if (!totals.canCheckout || !totals.input || totals.currencyId === null) return null
  return {
    input: totals.input,
    currencyId: totals.currencyId,
    notation: totals.notation,
    subtotal: totals.subtotal,
    discountAmount: totals.discountAmount,
    total: totals.total,
    hasInsufficientStock: totals.hasInsufficientStock,
    currencies: totals.currencies,
    totalsByCurrency: totals.totalsByCurrency,
    lineCount: totals.input.lines.length,
  }
}

/**
 * Checkout (track P4): mounted once at the root of the till. Answers the `checkout` command (Checkout button, Ctrl+Enter) by opening the
 * payment dialog on the server-priced cart, and the `recentSales` command (reprint). It renders no layout of its own.
 *
 * The cart is cleared only after the server confirmed the sale: a refusal or a lost connection leaves the cart exactly as it was.
 */
export default function CheckoutSlot() {
  const { t } = useTranslation('pos')
  const dialog = useCheckoutUi((s) => s.dialog)
  const totals = useCartTotals()
  const afterSale = useAfterSale()
  const canSeeSales = useCan(Permissions.Orders_View)

  // The command handlers are registered once; they read the latest figures from here.
  const latest = useRef({ totals, canSeeSales, afterSale, t })
  useEffect(() => {
    latest.current = { totals, canSeeSales, afterSale, t }
  })

  const close = useCallback(() => {
    useCheckoutUi.getState().close()
    posBus.focusScan()
  }, [])

  useEffect(() => {
    const offs = [
      posBus.onCommand('checkout', () => {
        const { totals: current, t: tr } = latest.current
        if (useCheckoutUi.getState().dialog) return
        if (useCartStore.getState().lines.length === 0) {
          toast.warning(tr('cartIsEmpty'))
          return
        }
        const snapshot = snapshotOf(current)
        if (!snapshot) {
          // Not priced yet (or refused / unreachable): the cart panel already says which; the shortcut gets a short word.
          toast.warning(current.error ? current.error.message : tr('checkoutPriceWait'))
          return
        }
        useCheckoutUi.getState().open({ kind: 'pay', snapshot })
      }),
      posBus.onCommand('recentSales', () => {
        const { canSeeSales: allowed, t: tr } = latest.current
        if (!allowed) {
          toast.warning(tr('lastSalesNotAllowed'))
          return
        }
        if (!useCheckoutUi.getState().dialog) useCheckoutUi.getState().open({ kind: 'recent' })
      }),
    ]
    return () => offs.forEach((off) => off())
  }, [])

  const sold = (sale: PosSale, received: number | null, change: number) => {
    const heldCartId = useCartStore.getState().heldCartId
    forgetAttempt()
    resetCart()
    latest.current.afterSale()
    // A cart that was resumed from the held list is sold now: take it off the list so it cannot be sold twice (best effort, silent).
    if (heldCartId !== null) {
      void http
        .delete<void>(`/carts/${heldCartId}`)
        .catch(() => undefined)
        .then(() => latest.current.afterSale())
    }
    if (getShowSaleComplete()) {
      useCheckoutUi.getState().open({ kind: 'done', finished: { sale, received, change } })
      return
    }
    // The popup is switched off on this computer: close the payment dialog and say it in a toast. What the popup would have flagged
    // (change to hand back, a receipt that did not print) still reaches the cashier.
    const { t: tr } = latest.current
    const notation = sale.currencyNotation
    close()
    toast.success(change > 0 ? tr('saleCompleteToastChange', { total: money(sale.total, notation), change: money(change, notation) }) : tr('saleCompleteToast', { total: money(sale.total, notation) }))
    if (sale.receiptPrinted === false) toast.warning(tr('receiptNotPrinted'), 8000)
  }

  if (!dialog) return null
  if (dialog.kind === 'pay') return <PaymentDialog snapshot={dialog.snapshot} onSold={sold} onClose={close} />
  if (dialog.kind === 'done') return <SaleCompleteDialog finished={dialog.finished} onClose={close} />
  return <RecentSalesDialog onClose={close} />
}
