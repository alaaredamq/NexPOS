import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { DateField } from '@/ui/form/DateField'
import { FormError } from '@/ui/form/FormError'
import { MoneyField } from '@/ui/form/MoneyField'
import { SelectField } from '@/ui/form/SelectField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { toDateInputValue } from '@/ui/format'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { MANUAL_TYPES, useAddMovement, type ManualType, type Wallet } from './api'
import styles from './wallet.module.css'

interface FormValues {
  type: ManualType
  moneyIn: boolean
  amount: number | null
  currencyId: number
  reference: string
  notes: string
  date: string
}

interface Props {
  wallet: Wallet
  onClose: () => void
}

/** "Record money in or out": anything that moved the Whish balance other than a sale (sales are saved by checkout). */
export function MovementModal({ wallet, onClose }: Props) {
  const { t } = useTranslation('wallet')
  const add = useAddMovement()
  const [formErrors, setFormErrors] = useState<string[]>([])
  const [today] = useState(() => toDateInputValue(new Date()))

  const schema = useMemo(
    () =>
      z.object({
        type: z.enum(MANUAL_TYPES),
        moneyIn: z.boolean(),
        amount: z.number({ message: t('amountInvalid') }).positive(t('amountInvalid')),
        currencyId: z.number(),
        reference: z.string().max(100),
        notes: z.string().max(500),
        date: z.string(),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema),
    defaultValues: {
      type: 'Withdrawal',
      moneyIn: true,
      amount: null,
      currencyId: wallet.balances[0]?.currencyId ?? 1,
      reference: '',
      notes: '',
      date: today,
    },
  })

  const type = useWatch({ control: form.control, name: 'type' })
  const moneyIn = useWatch({ control: form.control, name: 'moneyIn' })
  const currencyId = useWatch({ control: form.control, name: 'currencyId' })
  const notation = wallet.balances.find((b) => b.currencyId === currencyId)?.currencyNotation ?? ''

  const typeOptions = MANUAL_TYPES.map((value) => ({ value, label: t(`type_${value}`) }))
  const currencyOptions = wallet.balances.map((b) => ({ value: b.currencyId, label: `${b.currencyCode} (${b.currencyNotation})` }))

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await add.mutateAsync({
        id: wallet.id,
        type: values.type,
        amount: values.amount ?? 0,
        moneyIn: values.moneyIn,
        currencyId: values.currencyId,
        reference: values.reference.trim() || null,
        notes: values.notes.trim() || null,
        date: values.date && values.date !== today ? values.date : null,
      })
      toast.success(t('saved'))
      onClose()
    } catch (error) {
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('saveFailed')])
    }
  })

  return (
    <Modal onClose={onClose} width={500} title={t('movementTitle')}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <SelectField<FormValues, ManualType> control={form.control} name="type" label={t('movementType')} options={typeOptions} />
            {type === 'Adjustment' && (
              <div className={styles.fieldGroup}>
                <span className={styles.fieldLabel}>{t('direction')}</span>
                <ChipGroup
                  aria-label={t('direction')}
                  options={[
                    { value: 'in', label: t('moneyIn') },
                    { value: 'out', label: t('moneyOut') },
                  ]}
                  value={moneyIn ? 'in' : 'out'}
                  onChange={(value) => form.setValue('moneyIn', value === 'in')}
                />
              </div>
            )}
            <div className={styles.row}>
              <MoneyField<FormValues> control={form.control} name="amount" label={t('amount')} currency={notation} required autoFocus selectOnFocus />
              {currencyOptions.length > 1 && (
                <SelectField<FormValues, number> control={form.control} name="currencyId" label={t('currency')} options={currencyOptions} />
              )}
            </div>
            <TextField<FormValues> control={form.control} name="reference" label={t('reference')} maxLength={100} dir="ltr" />
            <TextField<FormValues>
              control={form.control}
              name="notes"
              label={type === 'SupplierPayment' ? t('notesSupplier') : t('notes')}
              multiline
              rows={2}
              maxLength={500}
            />
            <DateField<FormValues> control={form.control} name="date" label={t('date')} hint={t('dateHint')} max={today} />
            {type === 'Adjustment' && <p className={styles.tip}>{t('openingTip')}</p>}
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={add.isPending}>
            {t('cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={add.isPending}>
            {t('save')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
