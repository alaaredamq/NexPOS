import { useCallback, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { http, isApiError } from '@/api/http'
import type { components } from '@/api/schema'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { Select, type SelectOption } from '@/ui/Select'
import { toast } from '@/ui/toast'
import { searchCustomers } from '../pos/checkout/api'
import sales from './sales.module.css'

export type SaleCustomerResult = components['schemas']['SaleCustomerResultDto']

interface Props {
  /** Which screen's endpoint to call: an order and its receipt are the same sale, so both reach the same server rule. */
  kind: 'orders' | 'receipts'
  id: number
  /** The order number or receipt number, for the title. */
  number: string
  customerId: number | null
  customerName: string | null
  onClose: () => void
}

/** The change moves orders, receipts and - for a sale on the customer's account - two customers' balances, so all of those go stale. */
function useChangeSaleCustomer(kind: Props['kind']) {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, customerId }: { id: number; customerId: number | null }) => http.put<SaleCustomerResult>(`/${kind}/${id}/customer`, { customerId }),
    onSuccess: () => {
      for (const key of ['orders', 'receipts', 'customers', 'cash-drawer']) void client.invalidateQueries({ queryKey: [key] })
    },
  })
}

/**
 * Picks the customer a sale should be in the name of (empty = walk-in). The server moves the order and its receipts together, and takes a sale
 * that was put on a customer's account along with it, so the dialog says so; it refuses to turn such a sale into a walk-in sale.
 */
export function ChangeCustomerDialog({ kind, id, number, customerId, customerName, onClose }: Props) {
  const { t } = useTranslation('common')
  const change = useChangeSaleCustomer(kind)
  const [error, setError] = useState<string[]>([])
  const initial: SelectOption<number> | null = customerId === null ? null : { value: customerId, label: customerName ?? `#${customerId}` }
  const [picked, setPicked] = useState<SelectOption<number> | null>(initial)
  const unchanged = (picked?.value ?? null) === customerId

  const load = useCallback(
    async (query: string) =>
      (await searchCustomers(query.trim())).map((c): SelectOption<number> => ({ value: c.id, label: c.name, hint: c.email || undefined })),
    [],
  )

  const submit = async () => {
    setError([])
    try {
      const result = await change.mutateAsync({ id, customerId: picked?.value ?? null })
      toast.success(result.debtMoved > 0 ? t('SaleCustomer_DoneMoved') : t('SaleCustomer_Done'))
      onClose()
    } catch (e) {
      setError([isApiError(e) ? e.message : t('SaleCustomer_Failed')])
    }
  }

  return (
    <Modal onClose={onClose} width={520} title={`${t('SaleCustomer_Change')} · ${number}`}>
      <ModalBody>
        <div className={sales.dialog}>
          <FormError messages={error} />
          <div>
            <div className={sales.itemLabel}>{t('SaleCustomer_Current')}</div>
            <div>{customerName ?? <span className={sales.muted}>{t('SaleCustomer_WalkIn')}</span>}</div>
          </div>
          <div>
            <div className={sales.itemLabel}>{t('SaleCustomer_New')}</div>
            <Select<number>
              value={picked}
              onChange={setPicked}
              loadOptions={load}
              placeholder={t('SaleCustomer_WalkIn')}
              aria-label={t('SaleCustomer_New')}
              clearable
              disabled={change.isPending}
            />
          </div>
          <p className={sales.help}>{t('SaleCustomer_Note')}</p>
        </div>
      </ModalBody>
      <ModalFooter>
        <Button variant="ghost" onClick={onClose} disabled={change.isPending}>
          {t('Cancel')}
        </Button>
        <Button variant="primary" onClick={() => void submit()} loading={change.isPending} disabled={unchanged}>
          {t('SaleCustomer_Change')}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
