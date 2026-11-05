import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Field } from '@/ui/form/Field'
import field from '@/ui/form/field.module.css'
import { FormError } from '@/ui/form/FormError'
import { formatMoney } from '@/ui/format'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { useCancelPurchase, type PurchaseDetail } from './api'
import styles from './purchases.module.css'

/**
 * Cancelling takes the received stock back out and removes the purchase from the supplier balance. The server refuses when part of
 * the goods was already sold (409 `stock_already_sold`); its message is shown here and nothing changes.
 */
export function CancelPurchaseDialog({ purchase, onClose }: { purchase: Pick<PurchaseDetail, 'id' | 'number' | 'total' | 'amountPaid' | 'currencyNotation'>; onClose: () => void }) {
  const { t } = useTranslation(['purchases', 'common'])
  const cancel = useCancelPurchase()
  const [reason, setReason] = useState('')
  const [errors, setErrors] = useState<string[]>([])

  const submit = async () => {
    setErrors([])
    try {
      await cancel.mutateAsync({ id: purchase.id, reason: reason.trim() || null })
      toast.success(t('Cancelled', { number: purchase.number }))
      onClose()
    } catch (error) {
      setErrors([isApiError(error) ? error.message : t('common.actionFailed', { ns: 'common' })])
    }
  }

  return (
    <Modal onClose={onClose} width={520} title={t('CancelPurchaseTitle', { number: purchase.number })}>
      <ModalBody>
        <div className={styles.form}>
          <FormError messages={errors} />
          <p>{t('CancelExplain', { total: formatMoney(purchase.total, purchase.currencyNotation) })}</p>
          {purchase.amountPaid > 0 && <p className={styles.hint}>{t('CancelPaidWarning', { paid: formatMoney(purchase.amountPaid, purchase.currencyNotation) })}</p>}
          <Field label={t('CancelReason')}>
            {(a11y) => <textarea {...a11y} className={field.control} rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} autoFocus />}
          </Field>
        </div>
      </ModalBody>
      <ModalFooter>
        <Button variant="ghost" onClick={onClose} disabled={cancel.isPending}>
          {t('KeepPurchase')}
        </Button>
        <Button variant="danger" loading={cancel.isPending} onClick={() => void submit()}>
          {t('ConfirmCancel')}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
