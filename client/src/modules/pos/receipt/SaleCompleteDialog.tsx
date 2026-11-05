import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { Icon } from '@/ui/Icon'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { useReceiptPrinting } from '../../_shared/receiptActions'
import { money } from '../cart/cartUi'
import type { FinishedSale } from '../checkout/checkoutUi'
import { salePaths } from './paths'
import styles from '../checkout/checkout.module.css'

interface Props {
  finished: FinishedSale
  /** "New sale": closes the dialog; the till is already empty. */
  onClose: () => void
}

/**
 * Shown after the server confirmed the sale (the cart is already cleared by then). Leads with what the cashier needs at the counter: the
 * change to hand back. A receipt that did not reach the printer is said out loud, with the way to retry or save the PDF; the sale itself is
 * saved either way.
 */
export function SaleCompleteDialog({ finished, onClose }: Props) {
  const { t } = useTranslation('pos')
  const { sale, change } = finished
  const canReprint = useCan(Permissions.Orders_View)
  const printing = useReceiptPrinting()
  const paths = salePaths(sale.orderId)
  const notation = sale.currencyNotation

  // The retry after a receipt that did not print: once it works there is nothing left to say, so the popup closes (a plain extra copy keeps it open).
  const printReceipt = async () => {
    const printed = await printing.print(paths.print, paths.pdf)
    if (printed && sale.receiptPrinted === false) onClose()
  }

  return (
    <Modal onClose={onClose} width={520} title={t('saleComplete')} hideClose>
      <ModalBody>
        <div className={styles.dialogForm}>
          <div className={styles.success} aria-live="polite">
            <Icon name="check" size={28} />
            <strong dir="ltr">{money(sale.total, notation)}</strong>
            <span>
              {sale.addedToDebt
                ? t('addedToAccount', { name: sale.customerName ?? '' })
                : t('paidWith', { method: sale.paymentMethod })}
            </span>
          </div>

          {change > 0 && (
            <div className={styles.callout} data-tone="change">
              <span>{t('changeDue')}</span>
              <strong dir="ltr">{money(change, notation)}</strong>
            </div>
          )}

          <dl className={styles.facts}>
            <div>
              <dt>{t('receipt')}</dt>
              <dd dir="ltr">{sale.receiptNumber ?? sale.orderNumber}</dd>
            </div>
            {sale.customerName && !sale.addedToDebt && (
              <div>
                <dt>{t('customerLabel')}</dt>
                <dd>{sale.customerName}</dd>
              </div>
            )}
            {sale.discountAmount > 0 && (
              <div>
                <dt>{t('Discount')}</dt>
                <dd dir="ltr">-{money(sale.discountAmount, notation)}</dd>
              </div>
            )}
          </dl>

          {sale.replayed && (
            <div role="status" className={styles.notice} data-tone="info">
              {t('saleReplayed')}
            </div>
          )}
          {sale.receiptPrinted === false && (
            <div role="alert" className={styles.notice} data-tone="warning">
              {t('receiptNotPrinted')}
            </div>
          )}
        </div>
      </ModalBody>
      <ModalFooter>
        {canReprint && (
          <>
            <Button variant="ghost" onClick={() => void printing.downloadPdf(paths.pdf, `${sale.receiptNumber ?? sale.orderNumber}.pdf`)} disabled={printing.busy}>
              {t('downloadPdf')}
            </Button>
            <Button variant="secondary" onClick={() => void printReceipt()} loading={printing.busy}>
              {sale.receiptPrinted === false ? t('printRetry') : t('printReceipt')}
            </Button>
          </>
        )}
        <Button variant="primary" size="lg" onClick={onClose} data-autofocus>
          {t('newSale')}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
