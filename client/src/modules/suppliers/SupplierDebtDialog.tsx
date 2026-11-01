import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { formatMoney } from '@/ui/format'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useAddDebt, type Supplier } from './api'
import styles from './suppliers.module.css'

interface FormValues {
  amount: number | null
  note: string
}

interface Props {
  supplier: Pick<Supplier, 'id' | 'name' | 'balance' | 'currencyNotation'>
  onClose: () => void
}

/**
 * Records a manual debt entry: "I owe them more" raises the balance, "I paid them" lowers it (past zero it means they owe the
 * shop). The new balance is previewed, but the server computes and returns the real one.
 */
export function SupplierDebtDialog({ supplier, onClose }: Props) {
  const { t } = useTranslation(['suppliers', 'common'])
  const add = useAddDebt()
  const [iOweMore, setIOweMore] = useState(true)
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        amount: z.number({ message: t('AmountRequired', { ns: 'suppliers' }) }).gt(0, t('AmountRequired', { ns: 'suppliers' })),
        note: z.string().max(500, t('NoteTooLong', { ns: 'suppliers' })),
      }),
    [t],
  )
  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema), defaultValues: { amount: null, note: '' } })
  const amount = useWatch({ control: form.control, name: 'amount' }) ?? 0
  const after = Math.round((supplier.balance + (iOweMore ? amount : -amount)) * 100) / 100

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await add.mutateAsync({ supplierId: supplier.id, input: { amount: values.amount ?? 0, iOweMore, note: values.note.trim() || null } })
      toast.success(t('DebtUpdated', { ns: 'suppliers' }))
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        toast.error(t('SupplierNotFound', { ns: 'suppliers' }))
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  return (
    <Modal onClose={onClose} width={520} title={t('AddTransaction', { ns: 'suppliers' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <strong>
              <bdi>{supplier.name}</bdi>
            </strong>
            <div className={styles.directions} role="group" aria-label={t('Type', { ns: 'suppliers' })}>
              <button type="button" className={styles.direction} aria-pressed={iOweMore} onClick={() => setIOweMore(true)}>
                <strong>{t('DebtDialogPlusTitle', { ns: 'suppliers' })}</strong>
                <span>{t('DebtDialogPlusSub', { ns: 'suppliers' })}</span>
              </button>
              <button type="button" className={styles.direction} aria-pressed={!iOweMore} onClick={() => setIOweMore(false)}>
                <strong>{t('DebtDialogMinusTitle', { ns: 'suppliers' })}</strong>
                <span>{t('DebtDialogMinusSub', { ns: 'suppliers' })}</span>
              </button>
            </div>
            <NumberField<FormValues>
              control={form.control}
              name="amount"
              label={iOweMore ? t('DebtDialogIncrease', { ns: 'suppliers' }) : t('DebtDialogDecrease', { ns: 'suppliers' })}
              decimals={2}
              min={0}
              unit={supplier.currencyNotation || undefined}
              required
              autoFocus
              selectOnFocus
            />
            <TextField<FormValues> control={form.control} name="note" label={t('Note', { ns: 'suppliers' })} multiline rows={2} maxLength={500} />
            <div className={styles.preview} role="status">
              <span>{t('BalanceAfter', { ns: 'suppliers' })}</span>
              <strong>{formatMoney(after, supplier.currencyNotation)}</strong>
            </div>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={add.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={add.isPending}>
            {t('Save', { ns: 'common' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
