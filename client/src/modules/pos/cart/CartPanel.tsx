import { useEffect, useRef } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { Icon } from '@/ui/Icon'
import { IconButton } from '@/ui/IconButton'
import { toast } from '@/ui/toast'
import { formatPercent } from '@/ui/format'
import { useCartStore } from '../store/cartStore'
import { AmountDialog } from './AmountDialog'
import { CartLineRow } from './CartLineRow'
import { DiscountDialog } from './DiscountDialog'
import { HeldCartsDialog } from './HeldCartsDialog'
import { HoldDialog } from './HoldDialog'
import { PricePickerDialog } from './PricePickerDialog'
import { money, useCartUi } from './cartUi'
import { useCartCurrency, useCartTotals } from './useCartTotals'
import styles from './cart.module.css'

interface Props {
  /** Called when the cashier presses Checkout. Only reachable while the server has priced the cart (`canCheckout`). P4 opens the payment dialog here. */
  onCheckout: () => void
  /** Replaces the label of the checkout button (P4 may want "Pay"). */
  checkoutLabel?: string
}

/**
 * The cart of the till (track P3): lines with quantity / weight and price choice, the server-priced totals, discount, hold / retrieve,
 * undo and the currency toggle. It owns every cart dialog, so P2 only has to place it in the screen's cart slot and P4 only has to
 * answer `onCheckout`. Other parts of the screen open its dialogs through `useCartUi.getState().open(...)` and add products
 * with `addToCart()`.
 */
export function CartPanel({ onCheckout, checkoutLabel }: Props) {
  const { t } = useTranslation('pos')
  const lines = useCartStore((s) => s.lines)
  const discount = useCartStore((s) => s.discount)
  const canUndo = useCartStore((s) => s.undoStack.length > 0)
  const { clear, undo, setCurrency } = useCartStore.getState()
  const totals = useCartTotals()
  const { currency, currencies } = useCartCurrency()
  const dialog = useCartUi((s) => s.dialog)
  const amountRequest = useCartUi((s) => s.amountRequest)
  const { open, close } = useCartUi.getState()

  const notation = totals.notation || currency?.notation || ''
  const empty = lines.length === 0
  const otherCurrencies = currencies.filter((c) => c.id !== totals.currencyId)

  const nextCurrency = () => {
    if (currencies.length < 2) return
    const at = currencies.findIndex((c) => c.id === totals.currencyId)
    setCurrency(currencies[(at + 1) % currencies.length]!.id)
  }

  const askClear = async () => {
    const ok = await confirm({ title: t('areYouSure'), message: t('clearAllItems'), confirmLabel: t('yesClearIt'), danger: true })
    if (ok) {
      clear()
      toast.success(t('cartCleared'))
    }
  }

  // Told once, as a toast (a banner would eat room on small screens): when the cart first holds an item past its stock, and again only after
  // that has cleared. The sale itself is allowed, the stock count stops at zero.
  const stockShort = totals.hasInsufficientStock && totals.status === 'ready'
  const toldStockShort = useRef(false)
  useEffect(() => {
    if (stockShort && !toldStockShort.current) toast.warning(t('cartStockWarning'), 6000)
    if (stockShort || totals.status === 'ready') toldStockShort.current = stockShort
  }, [stockShort, totals.status, t])

  const priceDialogLine = dialog?.kind === 'price' ? lines.find((l) => l.productId === dialog.productId) : undefined

  return (
    <section className={styles.panel} aria-label={t('ShoppingCart')}>
      <header className={styles.head}>
        <h2 className={styles.title}>
          {t('ShoppingCart')} <span className={styles.count}>{lines.length}</span>
        </h2>
        <div className={styles.headActions}>
          <button type="button" className={styles.textBtn} onClick={undo} disabled={!canUndo}>
            {t('cartUndo')}
          </button>
          <IconButton icon="trash" tone="danger" label={t('clearCart')} size="sm" onClick={() => void askClear()} disabled={empty} />
        </div>
      </header>

      {empty ? (
        <div className={styles.empty}>
          <Icon name="cart" size={32} />
          <strong>{t('CartIsEmpty')}</strong>
          <span>{t('StartAddingProductsToBegin')}</span>
        </div>
      ) : (
        <ul className={styles.lines} aria-label={t('ShoppingCart')}>
          {lines.map((line) => (
            <CartLineRow
              key={line.productId}
              line={line}
              view={totals.lines.get(line.productId)}
              notation={notation}
              currencyId={totals.currencyId}
              settled={totals.isServer}
            />
          ))}
        </ul>
      )}

      {totals.status === 'error' && totals.error && (
        <div className={styles.notice} data-tone="error" role="alert">
          <span>{totals.error.code === 'network_error' ? t('cartPriceUnreachable') : totals.error.message}</span>
          <Button size="md" onClick={totals.retry}>
            {t('priceRetry')}
          </Button>
        </div>
      )}

      <div className={styles.summary} aria-live="polite">
        {discount && totals.discountAmount > 0 && (
          <div className={styles.sumRow}>
            <span>{t('subtotal')}</span>
            <s dir="ltr">{money(totals.subtotal, notation)}</s>
          </div>
        )}
        {discount && totals.discountAmount > 0 && (
          <div className={styles.sumRow}>
            <span>
              {discount.label ?? t('Discount')} <bdi dir="ltr">({formatPercent(totals.discountPercentage, 2)})</bdi>
            </span>
            <span className={styles.discount} dir="ltr">
              −{money(totals.discountAmount, notation)}
            </span>
          </div>
        )}
        {totals.taxAmount > 0 && (
          <div className={styles.sumRow}>
            <span>{t('cartTax')}</span>
            <span dir="ltr">{money(totals.taxAmount, notation)}</span>
          </div>
        )}
        <div className={styles.totalRow} data-estimate={totals.isServer || empty ? undefined : true}>
          <span>{t('cartTotal')}</span>
          <strong className={styles.total} dir="ltr" data-testid="cart-total">
            {money(totals.total, notation)}
          </strong>
        </div>
        {!empty && otherCurrencies.length > 0 && (
          <div className={styles.others} dir="ltr">
            {otherCurrencies.map((c) => (
              <span key={c.id}>≈ {money(totals.totalsByCurrency[c.id] ?? 0, c.notation)}</span>
            ))}
          </div>
        )}
      </div>

      <div className={styles.actions}>
        <Button onClick={() => open({ kind: 'discount' })} disabled={empty}>
          {t('Discount')}
        </Button>
        <Button onClick={() => open({ kind: 'hold' })} disabled={empty || totals.input === null}>
          {t('Hold')}
        </Button>
        <Button onClick={() => open({ kind: 'held' })}>{t('Retrieve')}</Button>
        {currencies.length > 1 && (
          <Button onClick={nextCurrency} aria-label={t('cartCurrencyToggle', { currency: notation })}>
            {notation}
          </Button>
        )}
      </div>

      <Button variant="primary" size="lg" block className={styles.checkout} onClick={onCheckout} disabled={!totals.canCheckout}>
        {checkoutLabel ?? t('Checkout')}
      </Button>

      {dialog?.kind === 'discount' && totals.currencyId !== null && (
        <DiscountDialog currencyId={totals.currencyId} notation={notation} subtotal={totals.subtotal} onClose={close} />
      )}
      {dialog?.kind === 'hold' && totals.input && <HoldDialog input={totals.input} notation={totals.notation} onClose={close} />}
      {dialog?.kind === 'held' && <HeldCartsDialog notation={notation} onClose={close} />}
      {priceDialogLine && totals.currencyId !== null && (
        <PricePickerDialog
          line={priceDialogLine}
          unitPrice={totals.lines.get(priceDialogLine.productId)?.unitPrice ?? 0}
          currencyId={totals.currencyId}
          notation={notation}
          onClose={close}
        />
      )}
      {amountRequest && <AmountDialog request={amountRequest} />}
    </section>
  )
}
