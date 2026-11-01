import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { formatQty } from '@/ui/format'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useAdjustStock, type InventoryItem } from './api'
import styles from './inventory.module.css'

interface FormValues {
  quantity: number | null
  reason: string
}

interface Props {
  product: Pick<InventoryItem, 'id' | 'name' | 'currentStock' | 'unit'>
  onClose: () => void
}

/** Stock arithmetic to the precision the server keeps (decimals for kg / litres) without float noise. */
const round = (value: number) => Math.round(value * 10_000) / 10_000

/** Add or remove units of one product, with the new level previewed. The server has the last word (it checks again). */
export function AdjustStockModal({ product, onClose }: Props) {
  const { t } = useTranslation(['inventory', 'common'])
  const adjust = useAdjustStock()
  const [type, setType] = useState<'add' | 'remove'>('add')
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        quantity: z.number({ message: t('QuantityRequired', { ns: 'inventory' }) }).gt(0, t('QuantityRequired', { ns: 'inventory' })),
        reason: z.string().max(500, t('ReasonTooLong', { ns: 'inventory' })),
      }),
    [t],
  )

  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema), defaultValues: { quantity: null, reason: '' } })
  const quantity = useWatch({ control: form.control, name: 'quantity' }) ?? 0
  const next = round(type === 'add' ? product.currentStock + quantity : product.currentStock - quantity)
  const tooMuch = type === 'remove' && next < 0

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    if (tooMuch) return
    try {
      const result = await adjust.mutateAsync({
        productId: product.id,
        input: { type, quantity: values.quantity ?? 0, reason: values.reason.trim() || null },
      })
      toast.success(t('StockAdjusted', { ns: 'inventory', stock: formatQty(result.newStock) }))
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        toast.error(t('ProductNotFound', { ns: 'inventory' }))
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  return (
    <Modal onClose={onClose} width={520} title={t('AdjustStock', { ns: 'inventory' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <dl className={styles.product}>
              <div>
                <dt>{t('ProductName', { ns: 'inventory' })}</dt>
                <dd>
                  <bdi>{product.name}</bdi>
                </dd>
              </div>
              <div>
                <dt>{t('CurrentStock', { ns: 'inventory' })}</dt>
                <dd>
                  <bdi>
                    {formatQty(product.currentStock)}
                    {product.unit && <span className={styles.unit}>{product.unit}</span>}
                  </bdi>
                </dd>
              </div>
            </dl>
            <ChipGroup
              aria-label={t('AdjustmentType', { ns: 'inventory' })}
              value={type}
              onChange={(value) => setType(value === 'remove' ? 'remove' : 'add')}
              options={[
                { value: 'add', label: t('AddStock', { ns: 'inventory' }) },
                { value: 'remove', label: t('RemoveStock', { ns: 'inventory' }) },
              ]}
            />
            <NumberField<FormValues> control={form.control} name="quantity" label={t('Quantity', { ns: 'inventory' })} required autoFocus selectOnFocus unit={product.unit ?? undefined} />
            <TextField<FormValues> control={form.control} name="reason" label={t('ReasonForAdjustment', { ns: 'inventory' })} multiline rows={3} maxLength={500} placeholder={t('EnterReasonPlaceholder', { ns: 'inventory' })} />
            <div className={styles.preview} data-invalid={tooMuch} role="status">
              <span>{tooMuch ? t('NotEnoughStock', { ns: 'inventory', stock: formatQty(product.currentStock) }) : t('NewStock', { ns: 'inventory' })}</span>
              {!tooMuch && (
                <strong>
                  <bdi>{formatQty(next)}</bdi>
                </strong>
              )}
            </div>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={adjust.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={adjust.isPending} disabled={tooMuch}>
            {t('Save', { ns: 'common' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
