import { useId, useMemo, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { useLicense } from '@/core/license/license'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { NumericInput } from '@/ui/form/NumericInput'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { useDrawerStatus } from '@/modules/cash-drawer/useDrawerStatus'
import { money, formatPercent } from '../cart/cartUi'
import { CustomerPicker } from '../customer/CustomerPicker'
import { useCartStore } from '../store/cartStore'
import { postCheckout, usePaymentMethods, type PosCheckoutRequest, type PosSale } from './api'
import type { CheckoutSnapshot } from './checkoutUi'
import { changeDue, isTotalAcceptable, keyForSale, shortBy, tenderSuggestions, totalTolerance, typedDiscount } from './tender'
import styles from './checkout.module.css'

interface Props {
  snapshot: CheckoutSnapshot
  /** The sale went through. `received` is the cash handed over (null = exact / not cash). */
  onSold: (sale: PosSale, received: number | null, change: number) => void
  onClose: () => void
}

type Failure = { kind: 'refused' | 'unsure'; message: string; addToDebt: boolean }

/**
 * The payment dialog (the old "Complete Checkout" popup, plus the change calculator). One tender: the API takes a single payment method,
 * so a split payment is not offered. The cashier may type a lower total (that becomes a discount on the receipt) or nudge it for cash
 * rounding; the server decides what is acceptable and answers `total_mismatch` otherwise.
 *
 * Never loses the cart: it is plain store state and this dialog only reads it. A refusal leaves it open with the reason; a lost connection
 * keeps the same idempotency key so "Try again" cannot sell twice.
 */
export function PaymentDialog({ snapshot, onSold, onClose }: Props) {
  const { t } = useTranslation('pos')
  const methods = usePaymentMethods()
  const license = useLicense()
  const drawer = useDrawerStatus()
  const customerId = useCartStore((s) => s.customerId)
  const setCustomerId = useCartStore((s) => s.setCustomerId)
  const [notes, setNotes] = useState(() => useCartStore.getState().notes)
  const [chosenMethod, setMethodId] = useState<number | null>(null)
  const [amount, setAmount] = useState<number | null>(snapshot.total)
  const [received, setReceived] = useState<number | null>(null)
  const [sending, setSending] = useState(false)
  const [failure, setFailure] = useState<Failure | null>(null)
  const [stopped, setStopped] = useState(!license.allowsSales)
  // Once the cashier says "sell anyway" it stays said for the retries of this sale.
  const [allowOversell, setAllowOversell] = useState(false)
  const amountId = useId()
  const receivedId = useId()
  const notesId = useId()

  const list = methods.data
  // The cashier's pick, else the shop's default method (derived, so there is no render with nothing chosen once the list is in).
  const methodId = chosenMethod ?? (list ? (list.find((m) => m.isDefault) ?? list[0])?.id ?? null : null)

  const method = list?.find((m) => m.id === methodId) ?? null
  const { notation, total, subtotal } = snapshot
  const typed = amount ?? NaN
  const amountOk = amount !== null && isTotalAcceptable(typed, total)
  const lowered = amountOk ? typedDiscount(typed, total, subtotal) : null
  const cash = method?.requiresChange === true
  const owedNow = amountOk ? typed : total
  const receivedShort = cash && received !== null && received < owedNow ? shortBy(received, owedNow) : 0
  const change = cash && received !== null && received >= owedNow ? changeDue(received, owedNow) : 0
  const suggestions = useMemo(() => (cash ? tenderSuggestions(owedNow) : []), [cash, owedNow])
  const others = snapshot.currencies.filter((c) => c.id !== snapshot.currencyId)

  const submit = async (addToDebt: boolean, oversell = allowOversell) => {
    if (sending || amount === null || !amountOk) return
    if (!addToDebt && methodId === null) return
    const cart = snapshot.input
    const payment = { cart, customerId, paymentMethodId: addToDebt ? 0 : methodId!, addToDebt, totalAmount: amount }
    const request: PosCheckoutRequest = {
      idempotencyKey: keyForSale(payment),
      cart,
      customerId,
      paymentMethodId: payment.paymentMethodId,
      addToDebt,
      totalAmount: amount,
      notes: notes.trim() || null,
      printReceipt: true,
      allowInsufficientStock: oversell,
      requireOpenDrawer: false,
    }
    setSending(true)
    setFailure(null)
    try {
      const sale = await postCheckout(request)
      onSold(sale, !addToDebt && cash ? received : null, !addToDebt ? change : 0)
    } catch (error) {
      setSending(false)
      if (!isApiError(error)) throw error
      if (error.code === 'insufficient_stock') {
        const sellAnyway = await confirm({
          title: t('insufficientStockTitle'),
          message: `${error.message} ${t('sellAnywayPrompt')}`,
          confirmLabel: t('sellAnyway'),
          cancelLabel: t('reviewCart'),
        })
        if (sellAnyway) {
          setAllowOversell(true)
          void submit(addToDebt, true)
        } else {
          onClose() // back to the cart to fix the quantity
        }
        return
      }
      if (error.code === 'license_sales_stopped') {
        setStopped(true)
        return
      }
      // No answer at all (or a server fault): the sale may or may not exist. Retrying with the same key is safe either way.
      if (error.status === 0 || error.status >= 500) setFailure({ kind: 'unsure', message: t('checkoutUnsure'), addToDebt })
      else setFailure({ kind: 'refused', message: error.message, addToDebt })
    }
  }

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    // "Try again" repeats the attempt that got no answer (a sale on account stays a sale on account).
    void submit(failure?.kind === 'unsure' ? failure.addToDebt : false)
  }

  const guardedClose = () => {
    if (!sending) onClose()
  }

  if (stopped) {
    return (
      <Modal onClose={onClose} width={440} title={t('salesStoppedTitle')}>
        <ModalBody>
          <p className={styles.plain}>{t('salesStoppedBody')}</p>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose}>
            {t('reviewCart')}
          </Button>
          <Link to="/license" onClick={onClose} className={styles.linkButton}>
            {t('openLicense')}
          </Link>
        </ModalFooter>
      </Modal>
    )
  }

  const noMethods = list !== undefined && list.length === 0
  const canComplete = amountOk && methodId !== null && receivedShort === 0 && !sending
  const canDebt = amountOk && customerId !== null && !sending
  const retry = failure?.kind === 'unsure'

  return (
    <Modal onClose={guardedClose} width={520} title={t('paymentTitle')}>
      <form onSubmit={onSubmit} noValidate className={styles.form}>
        <ModalBody>
          <div className={styles.dialogForm}>
            <div className={styles.due} aria-live="polite">
              <span>{t('paymentTotalDue')}</span>
              <strong dir="ltr">{money(total, notation)}</strong>
            </div>
            {others.length > 0 && (
              <p className={styles.hint} dir="ltr">
                {others.map((c) => `≈ ${money(snapshot.totalsByCurrency[c.id] ?? 0, c.notation)}`).join('   ')}
              </p>
            )}

            {drawer.known && !drawer.isOpen && (
              <div role="status" className={styles.notice} data-tone="warning">
                <span>
                  <strong>{t('drawerClosedTitle')}</strong> {t('drawerClosedBody')}
                </span>
                <Link to="/cash-drawer/open" onClick={onClose} className={styles.noticeLink}>
                  {t('openDrawer')}
                </Link>
              </div>
            )}

            {failure && (
              <div role="alert" className={styles.notice} data-tone="error">
                {failure.message}
              </div>
            )}

            <CustomerPicker customerId={customerId} onChange={(id) => setCustomerId(id)} notation={notation} disabled={sending} />

            <div className={styles.fieldGroup}>
              <span className={styles.fieldLabel} id={`${amountId}-methods`}>
                {t('paymentMethodRequired')}
              </span>
              {methods.isPending ? (
                <Loader size="sm" tone="muted" />
              ) : methods.isError ? (
                <p className={styles.fieldError} role="alert">
                  {t('failedToLoadPaymentMethods')}
                </p>
              ) : noMethods ? (
                <p className={styles.fieldError} role="alert">
                  {t('noPaymentMethods')}
                </p>
              ) : (
                <div className={styles.methods} role="radiogroup" aria-labelledby={`${amountId}-methods`}>
                  {list!.map((m) => (
                    <button
                      key={m.id}
                      type="button"
                      role="radio"
                      aria-checked={m.id === methodId}
                      className={styles.method}
                      data-selected={m.id === methodId || undefined}
                      onClick={() => setMethodId(m.id)}
                      disabled={sending}
                    >
                      {m.name}
                    </button>
                  ))}
                </div>
              )}
            </div>

            <div className={styles.fieldGroup}>
              <label htmlFor={amountId} className={styles.fieldLabel}>
                {t('totalAmountEditable')}
              </label>
              <NumericInput
                id={amountId}
                value={amount}
                onChange={setAmount}
                decimals={2}
                padDecimals
                min={0}
                prefix={notation}
                selectOnFocus
                disabled={sending}
                aria-invalid={!amountOk}
                aria-describedby={`${amountId}-hint`}
              />
              <p id={`${amountId}-hint`} className={styles.hint} aria-live="polite">
                {amount === null || !(amount >= 0)
                  ? t('pleaseEnterValidAmount')
                  : !amountOk
                    ? t('totalOutOfRange', { total: money(total, notation), max: money(totalTolerance(total), notation) })
                    : lowered
                      ? `${t('discountWithPercent', { percent: formatPercent(lowered.percent) })}: -${money(lowered.amount, notation)}`
                      : `${t('original')}: ${money(total, notation)}`}
              </p>
            </div>

            {cash && (
              <div className={styles.fieldGroup}>
                <label htmlFor={receivedId} className={styles.fieldLabel}>
                  {t('amountReceived')}
                </label>
                <NumericInput
                  id={receivedId}
                  value={received}
                  onChange={setReceived}
                  decimals={2}
                padDecimals
                  min={0}
                  prefix={notation}
                  selectOnFocus
                  disabled={sending}
                  placeholder={money(owedNow, notation)}
                />
                <div className={styles.chips}>
                  {suggestions.map((figure) => (
                    <button key={figure} type="button" className={styles.chip} onClick={() => setReceived(figure)} disabled={sending}>
                      {figure === owedNow ? t('cashExact') : money(figure, notation)}
                    </button>
                  ))}
                </div>
                <div className={styles.callout} data-tone={receivedShort > 0 ? 'short' : change > 0 ? 'change' : undefined} aria-live="polite">
                  {receivedShort > 0 ? (
                    <>
                      <span>{t('receivedShort')}</span>
                      <strong dir="ltr">{money(receivedShort, notation)}</strong>
                    </>
                  ) : (
                    <>
                      <span>{t('changeDue')}</span>
                      <strong dir="ltr">{money(change, notation)}</strong>
                    </>
                  )}
                </div>
              </div>
            )}

            <div className={styles.fieldGroup}>
              <label htmlFor={notesId} className={styles.fieldLabel}>
                {t('notesOptional')}
              </label>
              <input id={notesId} className={styles.input} value={notes} maxLength={500} placeholder={t('addNotes')} onChange={(e) => setNotes(e.target.value)} disabled={sending} />
            </div>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={guardedClose} disabled={sending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button
            variant="secondary"
            onClick={() => void submit(true)}
            disabled={!canDebt}
            title={customerId === null ? t('selectCustomerForDebt') : undefined}
          >
            {t('addToDebt')}
          </Button>
          <Button type="submit" variant="primary" size="lg" loading={sending} disabled={!canComplete}>
            {retry ? t('tryAgain') : t('completeCheckout')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
