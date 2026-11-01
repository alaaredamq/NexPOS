import { useMemo } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { formatQty, ltrIsolate } from '@/ui/format'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useCurrencyOptions, useRateSuppliers, useSaveExchangeRate, type ExchangeRate } from './api'
import styles from './money.module.css'
import { useFormRunner } from './useFormRunner'

interface FormValues {
  fromCurrencyId: number | null
  toCurrencyId: number | null
  rate: number | null
  supplierId: number | null
}

interface Props {
  /** The rate being edited; omit to create one. */
  rate?: ExchangeRate
  onClose: () => void
}

/** Create / edit dialog: `1 from = rate to`, optionally only for one supplier's products. One rate per pair and supplier. */
export function ExchangeRateFormModal({ rate, onClose }: Props) {
  const { t } = useTranslation(['money', 'common'])
  const save = useSaveExchangeRate()
  const currencies = useCurrencyOptions().data?.items ?? []
  const suppliers = useRateSuppliers().data ?? []

  const schema = useMemo(
    () =>
      z
        .object({
          fromCurrencyId: z.number({ error: t('PickCurrency', { ns: 'money' }) }).nullable().refine((v) => v !== null, t('PickCurrency', { ns: 'money' })),
          toCurrencyId: z.number({ error: t('PickCurrency', { ns: 'money' }) }).nullable().refine((v) => v !== null, t('PickCurrency', { ns: 'money' })),
          rate: z.number({ error: t('RatePositive', { ns: 'money' }) }).nullable().refine((v) => v !== null && v > 0, t('RatePositive', { ns: 'money' })),
          supplierId: z.number().nullable(),
        })
        .refine((v) => v.fromCurrencyId === null || v.fromCurrencyId !== v.toCurrencyId, { path: ['toCurrencyId'], message: t('DifferentCurrencies', { ns: 'money' }) }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: {
      fromCurrencyId: rate?.fromCurrencyId ?? null,
      toCurrencyId: rate?.toCurrencyId ?? null,
      rate: rate?.rate ?? null,
      supplierId: rate?.supplierId ?? null,
    },
  })
  const { formErrors, run } = useFormRunner(form, t('common.actionFailed', { ns: 'common' }), () => {
    toast.error(t('RateNotFound', { ns: 'money' }))
    onClose()
  })

  const [from, to, value] = useWatch({ control: form.control, name: ['fromCurrencyId', 'toCurrencyId', 'rate'] })
  const code = (id: number | null) => currencies.find((c) => c.id === id)?.code
  const preview = from !== null && to !== null && value ? ltrIsolate(`1 ${code(from) ?? ''} = ${formatQty(value, 6)} ${code(to) ?? ''}`) : null

  const options = currencies.map((c) => ({ value: c.id, label: `${c.code} - ${c.name}` }))
  const supplierOptions = suppliers.map((s) => ({ value: s.id, label: s.name }))

  const submit = form.handleSubmit((values) =>
    run(async () => {
      await save.mutateAsync({
        id: rate?.id,
        input: {
          fromCurrencyId: values.fromCurrencyId as number,
          toCurrencyId: values.toCurrencyId as number,
          rate: values.rate as number,
          supplierId: values.supplierId,
        },
      })
      toast.success(rate ? t('RateUpdated', { ns: 'money' }) : t('RateCreated', { ns: 'money' }))
      onClose()
    }),
  )

  return (
    <Modal onClose={onClose} width={620} title={rate ? t('EditExchangeRate', { ns: 'money' }) : t('AddExchangeRate', { ns: 'money' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <p className={styles.note}>{t('ExchangeRateHelp', { ns: 'money' })}</p>
            <div className={styles.formRow}>
              <SelectField<FormValues, number> control={form.control} name="fromCurrencyId" label={t('FromCurrency', { ns: 'money' })} required options={options} />
              <SelectField<FormValues, number> control={form.control} name="toCurrencyId" label={t('ToCurrency', { ns: 'money' })} required options={options} />
            </div>
            <NumberField<FormValues> control={form.control} name="rate" decimals={6} min={0} label={t('Rate', { ns: 'money' })} hint={preview ?? t('RateHint', { ns: 'money' })} required />
            <SelectField<FormValues, number>
              control={form.control}
              name="supplierId"
              label={t('SupplierOptional', { ns: 'money' })}
              hint={t('SupplierRateHint', { ns: 'money' })}
              options={supplierOptions}
              placeholder={t('StoreWideRate', { ns: 'money' })}
              clearable
            />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {rate ? t('Save', { ns: 'common' }) : t('AddExchangeRate', { ns: 'money' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
