import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useUpdateWallet, type Wallet } from './api'
import styles from './wallet.module.css'

interface FormValues {
  name: string
  accountNumber: string
  isActive: boolean
}

/** The wallet's name, the shop's Whish number, and the on/off switch. */
export function SettingsModal({ wallet, onClose }: { wallet: Wallet; onClose: () => void }) {
  const { t } = useTranslation('wallet')
  const update = useUpdateWallet()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () => z.object({ name: z.string().trim().min(1, t('nameRequired')).max(100), accountNumber: z.string().max(50), isActive: z.boolean() }),
    [t],
  )
  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema),
    defaultValues: { name: wallet.name, accountNumber: wallet.accountNumber ?? '', isActive: wallet.isActive },
  })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await update.mutateAsync({ id: wallet.id, name: values.name.trim(), accountNumber: values.accountNumber.trim() || null, isActive: values.isActive })
      toast.success(t('saved'))
      onClose()
    } catch (error) {
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('saveFailed')])
    }
  })

  return (
    <Modal onClose={onClose} width={460} title={t('settingsTitle')}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <TextField<FormValues> control={form.control} name="name" label={t('name')} required maxLength={100} />
            <TextField<FormValues> control={form.control} name="accountNumber" label={t('setupNumber')} hint={t('setupNumberHint')} type="tel" maxLength={50} dir="ltr" />
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('isActive')} hint={t('isActiveHint')} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={update.isPending}>
            {t('cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={update.isPending}>
            {t('save')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
