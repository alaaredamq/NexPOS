import { useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { SelectField } from '@/ui/form/SelectField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSetOnline, type Wallet } from './api'
import styles from './wallet.module.css'

type Mode = 'Off' | 'Sandbox' | 'Live'

interface FormValues {
  mode: Mode
  channel: string
  secret: string
  websiteUrl: string
}

/** The keys Whish gives with a merchant account. The secret is never shown again; leaving it empty keeps the saved one. */
export function OnlineModal({ wallet, onClose }: { wallet: Wallet; onClose: () => void }) {
  const { t } = useTranslation('wallet')
  const save = useSetOnline()
  const [formErrors, setFormErrors] = useState<string[]>([])
  const online = wallet.online

  const schema = useMemo(
    () => z.object({ mode: z.enum(['Off', 'Sandbox', 'Live']), channel: z.string().max(200), secret: z.string().max(500), websiteUrl: z.string().max(300) }),
    [],
  )
  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema),
    defaultValues: {
      mode: (online.mode as Mode) ?? 'Off',
      channel: online.channel ?? '',
      secret: '',
      websiteUrl: online.websiteUrl ?? '',
    },
  })
  const mode = useWatch({ control: form.control, name: 'mode' })

  const modes = [
    { value: 'Off' as const, label: t('modeOff') },
    { value: 'Sandbox' as const, label: t('modeSandbox') },
    { value: 'Live' as const, label: t('modeLive') },
  ]

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await save.mutateAsync({
        id: wallet.id,
        mode: values.mode,
        channel: values.channel.trim() || null,
        secret: values.secret.trim() || null,
        websiteUrl: values.websiteUrl.trim() || null,
      })
      toast.success(t('saved'))
      onClose()
    } catch (error) {
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('saveFailed')])
    }
  })

  return (
    <Modal onClose={onClose} width={520} title={t('onlineTitle')}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <p className={styles.tip}>{t('onlineIntro')}</p>
            <FormError messages={formErrors} />
            <SelectField<FormValues, Mode> control={form.control} name="mode" label={t('onlineMode')} options={modes} />
            {mode !== 'Off' && (
              <>
                <TextField<FormValues> control={form.control} name="channel" label={t('onlineChannel')} required maxLength={200} dir="ltr" autoComplete="off" />
                <TextField<FormValues>
                  control={form.control}
                  name="secret"
                  type="password"
                  label={t('onlineSecret')}
                  hint={online.hasSecret ? t('onlineSecretKept') : undefined}
                  required={!online.hasSecret}
                  maxLength={500}
                  dir="ltr"
                  autoComplete="new-password"
                />
                <TextField<FormValues>
                  control={form.control}
                  name="websiteUrl"
                  type="url"
                  label={t('onlineWebsite')}
                  hint={t('onlineWebsiteHint')}
                  required
                  maxLength={300}
                  dir="ltr"
                />
                {mode === 'Sandbox' && <p className={styles.muted}>{t('onlineSandboxHint')}</p>}
              </>
            )}
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {t('save')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
