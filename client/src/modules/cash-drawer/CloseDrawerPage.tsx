import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { FormError } from '@/ui/form/FormError'
import { MoneyField } from '@/ui/form/MoneyField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { formatDateTime } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { StatTile } from '@/ui/StatTile'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { DRAWER_CURRENCY, useCloseDrawer, useCurrentDrawer, useDrawerSession } from './api'
import { differenceOf, needsNotesConfirmation } from './drawerMath'
import { money, signedMoney } from './money'
import styles from './cashDrawer.module.css'

interface FormValues {
  actualCash: number | null
  notes: string
}


/**
 * `/app/cash-drawer/close/:id?` - count the drawer and close the session. Without an id it closes the signed-in user's open
 * session (the Razor page did the same); with one it closes that session. The difference shown while typing is a preview:
 * the server works out and stores the real one.
 */
export default function CloseDrawerPage() {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const navigate = useNavigate()
  const routeId = useParams().id
  const current = useCurrentDrawer()
  const id = routeId === undefined ? (current.data?.session?.id ?? null) : Number(routeId)
  const query = useDrawerSession(id !== null && Number.isInteger(id) && id > 0 ? id : null)
  const close = useCloseDrawer()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        actualCash: z.number({ message: t('PleaseEnterValidActualCash', { ns: 'cashDrawer' }) }).min(0, t('PleaseEnterValidActualCash', { ns: 'cashDrawer' })),
        notes: z.string().max(1000),
      }),
    [t],
  )
  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema), defaultValues: { actualCash: null, notes: '' } })
  const counted = useWatch({ control: form.control, name: 'actualCash' })

  if (routeId === undefined && current.isPending) return <Loader fullSection label={t('Loading', { ns: 'cashDrawer' })} />
  if (id === null || !Number.isInteger(id) || id < 1 || (isApiError(query.error) && query.error.status === 404)) return <NoSession />
  if (query.isPending) return <Loader fullSection label={t('Loading', { ns: 'cashDrawer' })} />
  if (query.isError) {
    return (
      <div className={styles.page}>
        <div className={styles.card} role="alert">
          {query.error.message}
          <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      </div>
    )
  }

  const session = query.data
  if (!session.isOpen) return <NoSession alreadyClosedId={session.id} />

  const difference = counted === null ? null : differenceOf(session.expectedCash, counted)

  const doClose = async (values: FormValues) => {
    try {
      await close.mutateAsync({ id: session.id, actualCash: values.actualCash ?? 0, notes: values.notes.trim() || null })
      toast.success(t('CashDrawerClosedSuccessfully', { ns: 'cashDrawer' }))
      // The Razor page opened the Z report in a new tab and went to the dashboard; here the report is the next step.
      void navigate(`/cash-drawer/z-report/${session.id}`, { replace: true })
    } catch (error) {
      setFormErrors(
        isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('FailedToCloseCashDrawer', { ns: 'cashDrawer' })],
      )
    }
  }

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    const diff = differenceOf(session.expectedCash, values.actualCash ?? 0)
    if (needsNotesConfirmation(diff, values.notes)) {
      const go = await confirm({
        title: t('CloseCashDrawer', { ns: 'cashDrawer' }),
        message: t('SignificantCashDifferenceWarning', { ns: 'cashDrawer', 0: Math.abs(diff.amount).toFixed(2) }),
        confirmLabel: t('CloseCashDrawer', { ns: 'cashDrawer' }),
        cancelLabel: t('Cancel', { ns: 'cashDrawer' }),
        danger: true,
      })
      if (!go) return
    }
    await doClose(values)
  })

  return (
    <div className={styles.page}>
      <PageHeader title={t('CloseCashDrawer', { ns: 'cashDrawer' })}>
        <Button variant="ghost" onClick={() => void navigate('/cash-drawer')}>{t('Cancel', { ns: 'cashDrawer' })}</Button>
      </PageHeader>
      <p className={`${styles.narrow} ${styles.muted}`}>
        {t('SessionId', { ns: 'cashDrawer' })}: #{session.id} · {formatDateTime(session.openedAt)}
      </p>

      <div className={`${styles.narrow} ${styles.tiles}`}>
        <StatTile label={t('OpeningCash', { ns: 'cashDrawer' })} value={money(session.openingCash)} />
        <StatTile label={t('TotalSales', { ns: 'cashDrawer' })} value={money(session.totalSales)} tone="success" />
        <StatTile label={t('CashDrops', { ns: 'cashDrawer' })} value={money(session.totalCashDrops)} />
        <StatTile label={t('ExpectedCash', { ns: 'cashDrawer' })} value={money(session.expectedCash)} tone="info" />
      </div>

      <form className={`${styles.narrow} ${styles.card}`} onSubmit={submit} noValidate>
        <p className={styles.muted}>{t('CountCashCarefully', { ns: 'cashDrawer' })}</p>
        <FormError messages={formErrors} />
        <MoneyField<FormValues>
          control={form.control}
          name="actualCash"
          label={t('ActualCashInDrawer', { ns: 'cashDrawer' })}
          hint={`${t('ExpectedAmount', { ns: 'cashDrawer' })}: ${money(session.expectedCash)}`}
          currency={DRAWER_CURRENCY}
          required
          autoFocus
          selectOnFocus
        />

        {difference && (
          <div className={styles.result} data-kind={difference.kind} role="status" aria-live="polite">
            <div className={styles.resultBody}>
              <span className={styles.resultLabel}>
                {difference.kind === 'surplus'
                  ? t('CashOverage', { ns: 'cashDrawer' })
                  : difference.kind === 'shortage'
                    ? t('CashShortage', { ns: 'cashDrawer' })
                    : t('PerfectBalance', { ns: 'cashDrawer' })}
              </span>
              <span className={`${styles.resultAmount} tabular-nums`}>
                {signedMoney(difference.amount)}
              </span>
            </div>
            <strong>
              {difference.kind === 'surplus'
                ? t('Surplus', { ns: 'cashDrawer' })
                : difference.kind === 'shortage'
                  ? t('Deficit', { ns: 'cashDrawer' })
                  : t('Balanced', { ns: 'cashDrawer' })}
            </strong>
          </div>
        )}

        <TextField<FormValues>
          control={form.control}
          name="notes"
          label={t('ClosingNotes', { ns: 'cashDrawer' })}
          hint={t('ClosingNotesHint', { ns: 'cashDrawer' })}
          placeholder={t('ClosingNotesPlaceholder', { ns: 'cashDrawer' })}
          multiline
          rows={2}
          maxLength={1000}
        />
        <div className={styles.actionsEnd}>
          <Button variant="ghost" onClick={() => void navigate('/cash-drawer')} disabled={close.isPending}>
            {t('Cancel', { ns: 'cashDrawer' })}
          </Button>
          <Button type="submit" variant="primary" size="lg" loading={close.isPending}>
            {close.isPending ? t('ClosingProcessing', { ns: 'cashDrawer' }) : t('CloseCashDrawer', { ns: 'cashDrawer' })}
          </Button>
        </div>
      </form>
    </div>
  )
}

/** The Razor page redirected to the drawer page with an error here. */
function NoSession({ alreadyClosedId }: { alreadyClosedId?: number }) {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const navigate = useNavigate()
  return (
    <div className={styles.page}>
      <div className={`${styles.narrow} ${styles.card} ${styles.empty}`}>
        <h2>{t('NoOpenCashDrawerSession', { ns: 'cashDrawer' })}</h2>
        <div className={styles.actions}>
          {alreadyClosedId !== undefined && (
            <Button variant="primary" onClick={() => void navigate(`/cash-drawer/z-report/${alreadyClosedId}`)}>
              {t('ZReport', { ns: 'cashDrawer' })}
            </Button>
          )}
          <Button onClick={() => void navigate('/cash-drawer')}>{t('CashDrawerManagement', { ns: 'cashDrawer' })}</Button>
        </div>
      </div>
    </div>
  )
}
