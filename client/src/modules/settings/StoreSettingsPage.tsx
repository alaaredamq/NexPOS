import { useEffect, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { FormError } from '@/ui/form/FormError'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Loader } from '@/ui/Loader'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useResetStoreSettings, useSaveStoreSettings, useStoreSettings, type StoreSettings } from './api'
import { ReceiptPreview } from './ReceiptPreview'
import { SettingsLayout } from './SettingsLayout'
import styles from './settings.module.css'

type FormValues = StoreSettings

/** `/app/settings/store` - the business details and footer text printed on receipts, with a live preview. */
export default function StoreSettingsPage() {
  const query = useStoreSettings()

  return (
    <SettingsLayout>
      {query.isPending && <Loader fullSection />}
      {query.isError && <p className={styles.error}>{query.error.message}</p>}
      {query.data && <StoreForm initial={query.data} />}
    </SettingsLayout>
  )
}

function StoreForm({ initial }: { initial: StoreSettings }) {
  const { t } = useTranslation(['settings', 'common'])
  const save = useSaveStoreSettings()
  const reset = useResetStoreSettings()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        storeName: z
          .string()
          .refine((v) => v.trim().length > 0, t('Set_StoreNameRequired', { ns: 'common' }))
          .max(200),
        addressLine1: z.string().max(200),
        addressLine2: z.string().max(200),
        phone: z.string().max(50),
        taxId: z.string().max(50),
        website: z.string().max(200),
        receiptFooterMessage: z.string().max(500),
        footerLine1: z.string().max(200),
        footerLine2: z.string().max(200),
        footerLine3: z.string().max(200),
      }),
    [t],
  )

  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema as never), defaultValues: initial })
  const values = useWatch({ control: form.control }) as FormValues

  // Another tab or a reset replaced the stored values: show them (never while the owner is typing).
  useEffect(() => {
    if (!form.formState.isDirty) form.reset(initial)
  }, [initial, form])

  const submit = form.handleSubmit(async (input) => {
    setFormErrors([])
    try {
      const saved = await save.mutateAsync(input)
      form.reset(saved)
      toast.success(t('SettingsSavedSuccessfully', { ns: 'settings' }))
    } catch (error) {
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  const askReset = async () => {
    await confirm({
      title: t('Confirm', { ns: 'common' }),
      message: t('ConfirmResetToDefaults', { ns: 'settings' }),
      confirmLabel: t('ResetToDefaults', { ns: 'settings' }),
      danger: true,
      onConfirm: async () => {
        const defaults = await reset.mutateAsync()
        form.reset(defaults)
        setFormErrors([])
        toast.success(t('SettingsResetSuccessfully', { ns: 'settings' }))
      },
    })
  }

  return (
    <form onSubmit={submit} noValidate className={styles.columns}>
      <div className={styles.stack}>
        <p className={styles.note}>{t('StoreSettingsDescription', { ns: 'settings' })}</p>
        <FormError messages={formErrors} />

        <section className={styles.card} aria-labelledby="store-info">
          <h2 id="store-info" className={styles.cardTitle}>
            {t('StoreInformation', { ns: 'settings' })}
          </h2>
          <TextField<FormValues> control={form.control} name="storeName" label={t('StoreName', { ns: 'settings' })} hint={t('StoreNameHint', { ns: 'settings' })} required maxLength={200} autoComplete="off" />
          <TextField<FormValues> control={form.control} name="addressLine1" label={t('AddressLine1', { ns: 'settings' })} hint={t('AddressLine1Hint', { ns: 'settings' })} maxLength={200} autoComplete="off" />
          <TextField<FormValues> control={form.control} name="addressLine2" label={t('AddressLine2', { ns: 'settings' })} hint={t('AddressLine2Hint', { ns: 'settings' })} maxLength={200} autoComplete="off" />
          <div className={styles.row}>
            <TextField<FormValues> control={form.control} name="phone" type="tel" dir="ltr" label={t('Phone', { ns: 'settings' })} maxLength={50} autoComplete="off" />
            <TextField<FormValues> control={form.control} name="taxId" dir="ltr" label={t('TaxId', { ns: 'settings' })} hint={t('TaxIdHint', { ns: 'settings' })} maxLength={50} autoComplete="off" />
          </div>
          <TextField<FormValues> control={form.control} name="website" type="url" dir="ltr" label={t('Website', { ns: 'settings' })} placeholder="www.yourstore.com" maxLength={200} autoComplete="off" />
        </section>

        <section className={styles.card} aria-labelledby="receipt-text">
          <h2 id="receipt-text" className={styles.cardTitle}>
            {t('ReceiptSettings', { ns: 'settings' })}
          </h2>
          <TextField<FormValues> control={form.control} name="receiptFooterMessage" label={t('ReceiptFooterMessage', { ns: 'settings' })} hint={t('ReceiptFooterMessageHint', { ns: 'settings' })} maxLength={500} autoComplete="off" />
          <TextField<FormValues> control={form.control} name="footerLine1" label={t('FooterLine1', { ns: 'settings' })} maxLength={200} autoComplete="off" />
          <TextField<FormValues> control={form.control} name="footerLine2" label={t('FooterLine2', { ns: 'settings' })} maxLength={200} autoComplete="off" />
          <TextField<FormValues> control={form.control} name="footerLine3" label={t('FooterLine3', { ns: 'settings' })} maxLength={200} autoComplete="off" />
        </section>

        <div className={styles.actions}>
          <Button variant="ghost" onClick={() => void askReset()} disabled={save.isPending || reset.isPending}>
            {t('ResetToDefaults', { ns: 'settings' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending} disabled={!form.formState.isDirty}>
            {t('SaveChanges', { ns: 'settings' })}
          </Button>
        </div>
      </div>

      <section className={`${styles.card} ${styles.previewCard}`} aria-labelledby="receipt-preview">
        <h2 id="receipt-preview" className={styles.cardTitle}>
          {t('ReceiptPreview', { ns: 'settings' })}
        </h2>
        <ReceiptPreview values={values} />
      </section>
    </form>
  )
}
