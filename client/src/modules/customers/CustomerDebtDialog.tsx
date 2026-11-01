import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { MoneyField } from '@/ui/form/MoneyField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { formatMoney } from '@/ui/format'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useAddDebtTransaction, useCustomerSummary, type DebtResult } from './api'
import styles from './customers.module.css'

export interface CustomerDebtDialogProps {
  customer: {
    id: number
    /** Shown in the title ("Mona Haddad"). */
    name: string
    /** The balance now (positive: they owe the business). Lets the dialog warn before an overpayment; leave out when unknown. */
    balance?: number
  }
  /** Notation of the default currency. Left out, the dialog asks the API (`GET customers/summary`). */
  currencyNotation?: string
  onClose: () => void
  /** Called after the entry is saved (and the dialog's own toast), with the new balance. The dialog closes itself. */
  onSaved?: (result: DebtResult) => void
}

interface FormValues {
  amount: number | null
  note: string
}

/**
 * "Add debt transaction" - the debt ledger dialog of the Customers pages, exported for the POS (track P4) to reuse.
 * Two sign-marked cards choose the direction (balance up = the customer owes more; down = the customer paid, or the business owes them),
 * then the amount and an optional note. Needs `Customers.Edit`: gate the button that opens it with `useCan`.
 * There is no cap on a payment: paying more than is owed leaves the customer in credit, and the dialog says so before saving.
 */
export function CustomerDebtDialog({ customer, currencyNotation, onClose, onSaved }: CustomerDebtDialogProps) {
  const { t } = useTranslation(['customers', 'common'])
  const add = useAddDebtTransaction()
  const summary = useCustomerSummary(currencyNotation === undefined)
  const notation = currencyNotation ?? summary.data?.currencyNotation ?? ''
  const [owesMore, setOwesMore] = useState(true)
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        amount: z.number({ error: t('DebtAmountRequired', { ns: 'customers' }) }).min(0.01, t('DebtAmountRequired', { ns: 'customers' })).nullable().refine((v) => v !== null, t('DebtAmountRequired', { ns: 'customers' })),
        note: z.string().max(500, t('DebtNoteTooLong', { ns: 'customers' })),
      }),
    [t],
  )

  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema), defaultValues: { amount: null, note: '' } })
  const amount = useWatch({ control: form.control, name: 'amount' })
  const entered = amount !== null && amount > 0 ? amount : null

  // What the balance will be (display only - the server decides). A negative result is the business owing the customer.
  const balanceAfter = entered !== null && customer.balance !== undefined ? customer.balance + (owesMore ? entered : -entered) : null

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      const result = await add.mutateAsync({
        customerId: customer.id,
        input: { amount: values.amount!, customerOwesMore: owesMore, note: values.note.trim() || null },
      })
      toast.success(t('DebtSaved', { ns: 'customers' }))
      onSaved?.(result)
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        toast.error(t('CustomerNotFound', { ns: 'customers' }))
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  const card = (value: boolean, sign: string, title: string, sub: string) => (
    <label className={styles.dirCard} data-direction={value ? 'plus' : 'minus'} data-checked={owesMore === value}>
      <input className={styles.dirInput} type="radio" name="debt-direction" checked={owesMore === value} onChange={() => setOwesMore(value)} />
      <span className={styles.dirSign} aria-hidden="true">
        {sign}
      </span>
      <span className={styles.dirTitle}>{title}</span>
      <span className={styles.dirSub}>{sub}</span>
    </label>
  )

  return (
    <Modal onClose={onClose} width={460} title={t('AddDebtTransaction', { ns: 'customers' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <p className={styles.dialogFor}>
              <bdi>{customer.name}</bdi>
            </p>
            <FormError messages={formErrors} />
            <div role="radiogroup" aria-label={t('DebtDirection', { ns: 'customers' })} className={styles.dirGroup}>
              {card(true, '+', t('DebtCustomerOwesMe', { ns: 'customers' }), t('DebtCustomerOwesMeSub', { ns: 'customers' }))}
              {card(false, '−', t('DebtIGotPaid', { ns: 'customers' }), t('DebtIGotPaidSub', { ns: 'customers' }))}
            </div>
            <MoneyField<FormValues> control={form.control} name="amount" label={t('Amount', { ns: 'common' })} required currency={notation} autoFocus selectOnFocus />
            <p className={styles.effect} data-direction={owesMore ? 'plus' : 'minus'} aria-live="polite">
              {owesMore ? t('DebtEffectUp', { ns: 'customers' }) : t('DebtEffectDown', { ns: 'customers' })}{' '}
              <strong>{entered !== null ? formatMoney(entered, notation) : t('DebtTheAmount', { ns: 'customers' })}</strong>
            </p>
            {balanceAfter !== null && (
              <p className={styles.effect} data-direction={balanceAfter < 0 ? 'minus' : 'plus'} aria-live="polite">
                {balanceAfter < 0
                  ? t('DebtOverpaid', { ns: 'customers', amount: formatMoney(Math.abs(balanceAfter), notation) })
                  : t('DebtBalanceAfter', { ns: 'customers', amount: formatMoney(balanceAfter, notation) })}
              </p>
            )}
            <TextField<FormValues> control={form.control} name="note" label={t('Cust_Note', { ns: 'common' })} placeholder={t('DebtNotePlaceholder', { ns: 'customers' })} maxLength={500} />
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
