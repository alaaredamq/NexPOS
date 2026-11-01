import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { formatDateTime } from '@/ui/format'
import { useReceiptPrinting } from '../../_shared/receiptActions'
import { money } from '../cart/cartUi'
import { useRecentSales } from '../checkout/api'
import { salePaths } from './paths'
import styles from '../checkout/checkout.module.css'

/** "Reprint the last receipt": the signed-in cashier's latest sales (last 7 days), each with reprint and PDF. Needs Orders.View (the slot checks). */
export function RecentSalesDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('pos')
  const recent = useRecentSales()
  const printing = useReceiptPrinting()

  return (
    <Modal onClose={onClose} width={560} title={t('lastSales')}>
      <ModalBody>
        {recent.isPending ? (
          <div className={styles.center}>
            <Loader />
          </div>
        ) : recent.isError ? (
          <div role="alert" className={styles.notice} data-tone="error">
            {t('failedToLoadSales')}
            <Button variant="ghost" onClick={() => void recent.refetch()}>
              {t('tryAgain')}
            </Button>
          </div>
        ) : recent.data.length === 0 ? (
          <p className={styles.plain}>{t('noRecentSales')}</p>
        ) : (
          <ul className={styles.sales}>
            {recent.data.map((sale) => {
              const paths = salePaths(sale.orderId)
              return (
                <li key={sale.receiptId} className={styles.saleRow}>
                  <div className={styles.saleMain}>
                    <strong dir="ltr">{sale.receiptNumber}</strong>
                    <span className={styles.hint}>
                      <bdi>{formatDateTime(sale.orderDate)}</bdi>
                      {sale.customerName ? <> · <bdi>{sale.customerName}</bdi></> : null}
                    </span>
                  </div>
                  <strong dir="ltr" className={styles.saleTotal}>
                    {money(sale.total, sale.currencyNotation)}
                  </strong>
                  <Button variant="secondary" onClick={() => void printing.print(paths.print, paths.pdf)} disabled={printing.busy}>
                    {t('printReceipt')}
                  </Button>
                  <Button variant="ghost" onClick={() => void printing.downloadPdf(paths.pdf, `${sale.receiptNumber}.pdf`)} disabled={printing.busy}>
                    {t('downloadPdf')}
                  </Button>
                </li>
              )
            })}
          </ul>
        )}
      </ModalBody>
      <ModalFooter>
        <Button variant="primary" onClick={onClose}>
          {t('Close', { ns: 'common' })}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
