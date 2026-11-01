import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatDateTime } from '@/ui/format'
import styles from './settings.module.css'

export interface ReceiptPreviewValues {
  storeName: string
  addressLine1: string
  addressLine2: string
  phone: string
  taxId: string
  website: string
  receiptFooterMessage: string
  footerLine1: string
  footerLine2: string
  footerLine3: string
}

/** A mock slip that follows the form as it is typed, so the owner sees where each line lands. The real receipt is printed by the server. */
export function ReceiptPreview({ values }: { values: ReceiptPreviewValues }) {
  const { t } = useTranslation('settings')
  const [now] = useState(() => formatDateTime(new Date().toISOString()))
  const v = values

  return (
    <div className={styles.slip} dir="auto" aria-label={t('ReceiptPreview')} role="img">
      <div className={styles.slipCenter}>
        <p className={styles.slipName}>{v.storeName || t('StoreName')}</p>
        {v.addressLine1 && <p>{v.addressLine1}</p>}
        {v.addressLine2 && <p>{v.addressLine2}</p>}
        {v.phone && <p>{t('PreviewPhone', { value: v.phone })}</p>}
        {v.taxId && <p>{t('PreviewTaxId', { value: v.taxId })}</p>}
      </div>
      <hr className={styles.slipRule} />
      <p>{t('PreviewReceiptNo')}</p>
      <p>{t('PreviewDate', { date: now })}</p>
      <p>{t('PreviewCustomer')}</p>
      <hr className={styles.slipRule} />
      <p>
        <strong>{t('PreviewProduct')}</strong>
      </p>
      <p>1 x 10.00 = 10.00</p>
      <hr className={styles.slipRule} />
      <p>
        <strong>{t('PreviewTotal')}</strong>
      </p>
      <hr className={styles.slipRule} />
      <div className={styles.slipCenter}>
        {v.receiptFooterMessage && <p className={styles.slipThanks}>{v.receiptFooterMessage}</p>}
        {v.footerLine1 && <p>{v.footerLine1}</p>}
        {v.footerLine2 && <p>{v.footerLine2}</p>}
        {v.footerLine3 && <p>{v.footerLine3}</p>}
        {v.website && <p>{t('PreviewQuestions', { value: v.website })}</p>}
      </div>
    </div>
  )
}
