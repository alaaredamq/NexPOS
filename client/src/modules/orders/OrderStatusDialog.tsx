import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { Select, type SelectOption } from '@/ui/Select'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import sales from '../_shared/sales.module.css'
import { ORDER_STATUSES, STATUS_KEYS, STATUS_TONES, useUpdateOrderStatus } from './api'

interface Props {
  order: { id: number; orderNumber: string; status: number }
  onClose: () => void
}

/**
 * Changes an order's status. Cancelled is not a label: it reverses the whole sale (receipt voided, stock back, debt and drawer corrected),
 * so the dialog says so before the button does anything. Returned only changes the label - exactly as the old status form - and says that too.
 */
export function OrderStatusDialog({ order, onClose }: Props) {
  const { t } = useTranslation('orders')
  const update = useUpdateOrderStatus()
  const [status, setStatus] = useState<number>(order.status)
  const [error, setError] = useState<string[]>([])

  const options: SelectOption<number>[] = ORDER_STATUSES.map((s) => ({ value: s, label: t(STATUS_KEYS[s] as string) }))
  const cancelling = status === 4
  const unchanged = status === order.status

  const submit = async () => {
    setError([])
    try {
      const result = await update.mutateAsync({ id: order.id, status })
      const reversal = result.reversal
      toast.success(
        reversal
          ? t('details.cancelledDone', { 0: order.orderNumber, 1: reversal.stockLinesRestored })
          : t('OrderStatusUpdatedSuccessfully'),
      )
      onClose()
    } catch (e) {
      setError([isApiError(e) ? e.message : t('ErrorUpdatingOrderStatus')])
    }
  }

  return (
    <Modal onClose={onClose} width={520} title={t('UpdateOrderStatus')}>
      <ModalBody>
        <div className={sales.dialog}>
          <FormError messages={error} />
          <div>
            <div className={sales.itemLabel}>{t('CurrentStatus')}</div>
            <StatusBadge tone={STATUS_TONES[order.status] ?? 'neutral'} label={t(STATUS_KEYS[order.status] ?? 'Pending')} />
          </div>
          <div>
            <div className={sales.itemLabel}>{t('NewStatus')}</div>
            <Select
              value={options.find((o) => o.value === status) ?? null}
              onChange={(o) => o && setStatus(Number(o.value))}
              options={options}
              aria-label={t('NewStatus')}
            />
          </div>
          {cancelling && !unchanged && (
            <p className={sales.help} data-tone="danger" role="alert">
              {t('details.cancelWarning')}
            </p>
          )}
          {status === 5 && !unchanged && <p className={sales.help}>{t('details.returnedNote')}</p>}
        </div>
      </ModalBody>
      <ModalFooter>
        <Button variant="ghost" onClick={onClose} disabled={update.isPending}>
          {t('Cancel')}
        </Button>
        <Button variant={cancelling ? 'danger' : 'primary'} onClick={() => void submit()} loading={update.isPending} disabled={unchanged}>
          {cancelling ? t('details.cancelOrder') : t('UpdateStatus')}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
