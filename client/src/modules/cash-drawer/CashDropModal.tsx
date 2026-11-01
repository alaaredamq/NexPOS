import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { MoneyField } from '@/ui/form/MoneyField'
import { SelectField } from '@/ui/form/SelectField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { DRAWER_CURRENCY, useAddCashDrop } from './api'
import { dropReasonOptions } from './reasons'
import styles from './cashDrawer.module.css'

interface FormValues {
  amount: number | null
  reason: string
  notes: string
}

interface Props {
  sessionId: number
  onClose: () => void
}

/** "Record cash drop": take cash out of the open drawer to the safe or the bank. The reason values are the English words the old page stored. */
export function CashDropModal({ sessionId, onClose }: Props) {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const add = useAddCashDrop()
  const [formErrors, setFormErrors] = useState<string[]>([])
  const reasons = useMemo(() => dropReasonOptions(t), [t])

  const schema = useMemo(
    () =>
      z.object({
        amount: z.number({ message: t('PleaseEnterValidAmount', { ns: 'cashDrawer' }) }).positive(t('PleaseEnterValidAmount', { ns: 'cashDrawer' })),
        reason: z.string(),
        notes: z.string().max(1000),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema),
    defaultValues: { amount: null, reason: reasons[0]!.value, notes: '' },
  })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await add.mutateAsync({ id: sessionId, amount: values.amount ?? 0, reason: values.reason || null, notes: values.notes.trim() || null })
      toast.success(t('CashDropRecordedSuccess', { ns: 'cashDrawer' }))
      onClose()
    } catch (error) {
      setFormErrors(
        isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('FailedToRecordCashDrop', { ns: 'cashDrawer' })],
      )
    }
  })

  return (
    <Modal onClose={onClose} width={480} title={t('RecordCashDrop', { ns: 'cashDrawer' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <MoneyField<FormValues> control={form.control} name="amount" label={t('Amount', { ns: 'cashDrawer' })} currency={DRAWER_CURRENCY} required autoFocus selectOnFocus />
            <SelectField<FormValues, string> control={form.control} name="reason" label={t('Reason', { ns: 'cashDrawer' })} options={reasons} />
            <TextField<FormValues> control={form.control} name="notes" label={t('Notes', { ns: 'cashDrawer' })} multiline rows={3} maxLength={1000} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={add.isPending}>
            {t('Cancel', { ns: 'cashDrawer' })}
          </Button>
          <Button type="submit" variant="primary" loading={add.isPending}>
            {t('RecordCashDrop', { ns: 'cashDrawer' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
