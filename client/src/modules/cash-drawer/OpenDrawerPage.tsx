import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { safeReturnTo } from '@/core/auth/returnTo'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { MoneyField } from '@/ui/form/MoneyField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { PageHeader } from '@/ui/PageHeader'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { DRAWER_CURRENCY, useOpenDrawer } from './api'
import styles from './cashDrawer.module.css'

interface FormValues {
  openingCash: number | null
  notes: string
}

/**
 * `/app/cash-drawer/open` - the starting float. `?returnTo=/pos` sends the cashier back to where they were sent from
 * (the till's "open your drawer first" prompt) once the drawer is open.
 */
export default function OpenDrawerPage() {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const open = useOpenDrawer()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        openingCash: z.number({ message: t('PleaseEnterAValidOpeningCashAmount', { ns: 'cashDrawer' }) }).min(0, t('PleaseEnterAValidOpeningCashAmount', { ns: 'cashDrawer' })),
        notes: z.string().max(1000),
      }),
    [t],
  )

  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema), defaultValues: { openingCash: null, notes: '' } })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await open.mutateAsync({ openingCash: values.openingCash ?? 0, notes: values.notes.trim() || null })
      toast.success(t('CashDrawerOpenedSuccessfully', { ns: 'cashDrawer' }))
      void navigate(safeReturnTo(search.get('returnTo')) ?? '/cash-drawer')
    } catch (error) {
      setFormErrors(
        isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('FailedToOpenCashDrawer', { ns: 'cashDrawer' })],
      )
    }
  })

  return (
    <div className={styles.page}>
      <PageHeader title={t('OpenCashDrawer', { ns: 'cashDrawer' })} />
      <div className={`${styles.narrow} ${styles.card}`}>
        <p className={styles.muted}>{t('EnterTheStartingCashAmountInYourDrawerToBeginYourShift', { ns: 'cashDrawer' })}</p>
        <form className={styles.form} onSubmit={submit} noValidate>
          <FormError messages={formErrors} />
          <MoneyField<FormValues>
            control={form.control}
            name="openingCash"
            label={t('StartingCashAmount', { ns: 'cashDrawer' })}
            hint={t('CountAllBillsAndCoinsInYourDrawer', { ns: 'cashDrawer' })}
            currency={DRAWER_CURRENCY}
            required
            autoFocus
            selectOnFocus
          />
          <TextField<FormValues>
            control={form.control}
            name="notes"
            label={t('NotesOptional', { ns: 'cashDrawer' })}
            placeholder={t('AddAnyNotesAboutThisSession', { ns: 'cashDrawer' })}
            multiline
            rows={3}
            maxLength={1000}
          />
          <div className={styles.tip} role="note">
            <strong>{t('Tip', { ns: 'cashDrawer' })}:</strong>
            <span>{t('MakeSureToCountYourStartingCashCarefully', { ns: 'cashDrawer' })}</span>
          </div>
          <div className={styles.actionsEnd}>
            <Button variant="ghost" onClick={() => void navigate('/cash-drawer')} disabled={open.isPending}>
              {t('Cancel', { ns: 'cashDrawer' })}
            </Button>
            <Button type="submit" variant="primary" size="lg" loading={open.isPending}>
              {open.isPending ? t('Opening', { ns: 'cashDrawer' }) : t('OpenCashDrawer', { ns: 'cashDrawer' })}
            </Button>
          </div>
        </form>
      </div>

      <div className={`${styles.narrow} ${styles.card}`}>
        <h2 className={styles.cardTitle}>{t('OpeningChecklist', { ns: 'cashDrawer' })}</h2>
        <ul className={styles.checklist}>
          <li>{t('CountAllCashInDrawer', { ns: 'cashDrawer' })}</li>
          <li>{t('VerifyBillsAndCoins', { ns: 'cashDrawer' })}</li>
          <li>{t('CheckForDamagedBills', { ns: 'cashDrawer' })}</li>
          <li>{t('RecordStartingAmountAccurately', { ns: 'cashDrawer' })}</li>
          <li>{t('ReadyToProcessSales', { ns: 'cashDrawer' })}</li>
        </ul>
      </div>
    </div>
  )
}
