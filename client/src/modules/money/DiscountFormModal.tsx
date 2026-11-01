import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSaveDiscount, type Discount } from './api'
import styles from './money.module.css'
import { useFormRunner } from './useFormRunner'

interface FormValues {
  name: string
  rate: number | null
  isDefault: boolean
}

interface Props {
  /** The discount being edited; omit to create one. */
  discount?: Discount
  onClose: () => void
}

/** Create / edit dialog for a percentage discount the cashier can pick at the till. */
export function DiscountFormModal({ discount, onClose }: Props) {
  const { t } = useTranslation(['money', 'common'])
  const save = useSaveDiscount()

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().refine((v) => v.trim().length > 0, t('NameRequired', { ns: 'money' })).refine((v) => v.trim().length <= 100, t('NameTooLong', { ns: 'money' })),
        rate: z.number({ error: t('PercentRange', { ns: 'money' }) }).nullable().refine((v) => v !== null && v >= 0 && v <= 100, t('PercentRange', { ns: 'money' })),
        isDefault: z.boolean(),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: { name: discount?.name ?? '', rate: discount?.rate ?? null, isDefault: discount?.isDefault ?? false },
  })
  const { formErrors, run } = useFormRunner(form, t('common.actionFailed', { ns: 'common' }), () => {
    toast.error(t('DiscountNotFound', { ns: 'money' }))
    onClose()
  })

  const submit = form.handleSubmit((values) =>
    run(async () => {
      await save.mutateAsync({ id: discount?.id, input: { name: values.name.trim(), rate: values.rate as number, isDefault: values.isDefault } })
      toast.success(discount ? t('DiscountUpdated', { ns: 'money' }) : t('DiscountCreated', { ns: 'money' }))
      onClose()
    }),
  )

  return (
    <Modal onClose={onClose} width={520} title={discount ? t('EditDiscount', { ns: 'money' }) : t('AddDiscount', { ns: 'money' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <TextField<FormValues> control={form.control} name="name" label={t('DiscountName', { ns: 'money' })} required maxLength={100} autoFocus autoComplete="off" />
            <NumberField<FormValues> control={form.control} name="rate" decimals={2} min={0} max={100} unit="%" label={t('DiscountPercent', { ns: 'money' })} required />
            <SwitchField<FormValues> control={form.control} name="isDefault" label={t('DefaultDiscount', { ns: 'money' })} hint={t('DefaultDiscountHint', { ns: 'money' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {discount ? t('Save', { ns: 'common' }) : t('AddDiscount', { ns: 'money' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
