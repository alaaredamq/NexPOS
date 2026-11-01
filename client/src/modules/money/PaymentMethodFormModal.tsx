import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSavePaymentMethod, type PaymentMethod } from './api'
import styles from './money.module.css'
import { useFormRunner } from './useFormRunner'

interface FormValues {
  name: string
  isDefault: boolean
}

interface Props {
  /** The method being edited; omit to create one. */
  method?: PaymentMethod
  onClose: () => void
}

/** Create / edit dialog for a way to pay. A name containing "cash" makes the till work out change. */
export function PaymentMethodFormModal({ method, onClose }: Props) {
  const { t } = useTranslation(['money', 'common'])
  const save = useSavePaymentMethod()

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().refine((v) => v.trim().length > 0, t('NameRequired', { ns: 'money' })).refine((v) => v.trim().length <= 100, t('NameTooLong', { ns: 'money' })),
        isDefault: z.boolean(),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: { name: method?.name ?? '', isDefault: method?.isDefault ?? false },
  })
  const { formErrors, run } = useFormRunner(form, t('common.actionFailed', { ns: 'common' }), () => {
    toast.error(t('PaymentMethodNotFound', { ns: 'money' }))
    onClose()
  })

  const submit = form.handleSubmit((values) =>
    run(async () => {
      await save.mutateAsync({ id: method?.id, input: { name: values.name.trim(), isDefault: values.isDefault } })
      toast.success(method ? t('PaymentMethodUpdated', { ns: 'money' }) : t('PaymentMethodCreated', { ns: 'money' }))
      onClose()
    }),
  )

  return (
    <Modal onClose={onClose} width={520} title={method ? t('EditPaymentMethod', { ns: 'money' }) : t('AddPaymentMethod', { ns: 'money' })}>
      <form onSubmit={submit} noValidate className={styles.modalForm}>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <TextField<FormValues>
              control={form.control}
              name="name"
              label={t('PaymentMethodName', { ns: 'money' })}
              hint={t('CashNameHint', { ns: 'money' })}
              required
              maxLength={100}
              autoFocus
              autoComplete="off"
            />
            <SwitchField<FormValues> control={form.control} name="isDefault" label={t('DefaultPaymentMethod', { ns: 'money' })} hint={t('DefaultPaymentMethodHint', { ns: 'money' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {method ? t('Save', { ns: 'common' }) : t('AddPaymentMethod', { ns: 'money' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
