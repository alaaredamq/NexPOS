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
import { useSaveCurrency, type Currency } from './api'
import styles from './money.module.css'
import { useFormRunner } from './useFormRunner'

interface FormValues {
  code: string
  name: string
  notation: string
  rateToUsd: number | null
  isDefault: boolean
  isActive: boolean
}

interface Props {
  /** The currency being edited; omit to create one. */
  currency?: Currency
  onClose: () => void
}

/** Create / edit dialog. The rate is "how many of this currency one US dollar buys" - the figure the till converts with. */
export function CurrencyFormModal({ currency, onClose }: Props) {
  const { t } = useTranslation(['money', 'common'])
  const save = useSaveCurrency()
  const isDefault = currency?.isDefault ?? false

  const schema = useMemo(
    () =>
      z.object({
        code: z.string().refine((v) => v.trim().length > 0, t('CodeRequired', { ns: 'money' })).refine((v) => v.trim().length <= 10, t('CodeTooLong', { ns: 'money' })),
        name: z.string().refine((v) => v.trim().length > 0, t('NameRequired', { ns: 'money' })),
        notation: z.string().refine((v) => v.trim().length > 0, t('SymbolRequired', { ns: 'money' })),
        rateToUsd: z.number({ error: t('RatePositive', { ns: 'money' }) }).nullable().refine((v) => v !== null && v > 0, t('RatePositive', { ns: 'money' })),
        isDefault: z.boolean(),
        isActive: z.boolean(),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: {
      code: currency?.code ?? '',
      name: currency?.name ?? '',
      notation: currency?.notation ?? '',
      rateToUsd: currency?.rateToUsd ?? null,
      isDefault,
      isActive: currency?.isActive ?? true,
    },
  })
  const { formErrors, run } = useFormRunner(form, t('common.actionFailed', { ns: 'common' }), () => {
    toast.error(t('CurrencyNotFound', { ns: 'money' }))
    onClose()
  })

  const submit = form.handleSubmit((values) =>
    run(async () => {
      await save.mutateAsync({
        id: currency?.id,
        input: {
          code: values.code.trim(),
          name: values.name.trim(),
          notation: values.notation.trim(),
          rateToUsd: values.rateToUsd as number,
          isDefault: values.isDefault,
          isActive: values.isActive,
        },
      })
      toast.success(currency ? t('CurrencyUpdated', { ns: 'money' }) : t('CurrencyCreated', { ns: 'money' }))
      onClose()
    }),
  )

  return (
    <Modal onClose={onClose} width={620} title={currency ? t('EditCurrency', { ns: 'money' }) : t('AddCurrency', { ns: 'money' })}>
      <form onSubmit={submit} noValidate className={styles.modalForm}>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <div className={styles.formRow}>
              <TextField<FormValues> control={form.control} name="code" dir="ltr" label={t('CurrencyCode', { ns: 'money' })} hint={t('CurrencyCodeHint', { ns: 'money' })} required maxLength={10} autoFocus autoComplete="off" />
              <TextField<FormValues> control={form.control} name="notation" dir="auto" label={t('CurrencySymbol', { ns: 'money' })} hint={t('CurrencySymbolHint', { ns: 'money' })} required maxLength={10} autoComplete="off" />
            </div>
            <TextField<FormValues> control={form.control} name="name" label={t('CurrencyName', { ns: 'money' })} required maxLength={100} autoComplete="off" />
            <NumberField<FormValues> control={form.control} name="rateToUsd" decimals={6} min={0} label={t('RateToUsd', { ns: 'money' })} hint={t('RateToUsdHint', { ns: 'money' })} required />
            <p className={styles.warn}>{t('RateChangeWarning', { ns: 'money' })}</p>
            <SwitchField<FormValues>
              control={form.control}
              name="isDefault"
              label={t('DefaultCurrency', { ns: 'money' })}
              hint={isDefault ? t('DefaultStaysHint', { ns: 'money' }) : t('DefaultCurrencyHint', { ns: 'money' })}
              disabled={isDefault}
            />
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('Active', { ns: 'common' })} hint={t('CurrencyActiveHint', { ns: 'money' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {currency ? t('Save', { ns: 'common' }) : t('AddCurrency', { ns: 'money' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
