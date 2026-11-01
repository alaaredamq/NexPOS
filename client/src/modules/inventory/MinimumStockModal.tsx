import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSetMinimumStock, type InventoryItem } from './api'
import styles from './inventory.module.css'

interface FormValues {
  minimumStock: number | null
}

interface Props {
  product: Pick<InventoryItem, 'id' | 'name' | 'minimumStock' | 'unit'>
  onClose: () => void
}

/** The level under which a product counts as low stock (it feeds the low-stock tile and report). */
export function MinimumStockModal({ product, onClose }: Props) {
  const { t } = useTranslation(['inventory', 'common'])
  const save = useSetMinimumStock()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () => z.object({ minimumStock: z.number({ message: t('MinimumRequired', { ns: 'inventory' }) }).min(0, t('MinimumRequired', { ns: 'inventory' })) }),
    [t],
  )
  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema), defaultValues: { minimumStock: product.minimumStock ?? 0 } })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await save.mutateAsync({ productId: product.id, minimumStock: values.minimumStock ?? 0 })
      toast.success(t('MinimumStockLevelUpdatedSuccessfully', { ns: 'inventory' }))
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
    <Modal onClose={onClose} width={440} title={t('SetMinimumStock', { ns: 'inventory' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <strong>
              <bdi>{product.name}</bdi>
            </strong>
            <NumberField<FormValues>
              control={form.control}
              name="minimumStock"
              label={t('MinimumStockLevel', { ns: 'inventory' })}
              placeholder={t('EnterMinimumStockLevel', { ns: 'inventory' })}
              required
              autoFocus
              selectOnFocus
              min={0}
              unit={product.unit ?? undefined}
            />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {t('Save', { ns: 'common' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
