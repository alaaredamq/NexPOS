import { useEffect, useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { formatNumber, ltrIsolate } from '@/ui/format'
import { zodResolver } from '../_shared/zodResolver'
import { useConvert, useCurrencyOptions } from './api'
import styles from './money.module.css'

interface FormValues {
  amount: number | null
  from: number | null
  to: number | null
}

/**
 * "What would the till show for this amount?" - asks the server, which converts through USD and rounds to 2 decimals exactly like a till price,
 * so after changing a rate you can check the result before a cashier meets it. Changes nothing.
 */
export function CurrencyConverter() {
  const { t } = useTranslation('money')
  const loaded = useCurrencyOptions().data
  const currencies = useMemo(() => loaded?.items ?? [], [loaded])
  const convert = useConvert()

  const schema = useMemo(
    () =>
      z.object({
        amount: z.number({ error: t('AmountRequired') }).nullable().refine((v) => v !== null && v >= 0, t('AmountRequired')),
        from: z.number({ error: t('PickCurrency') }).nullable().refine((v) => v !== null, t('PickCurrency')),
        to: z.number({ error: t('PickCurrency') }).nullable().refine((v) => v !== null, t('PickCurrency')),
      }),
    [t],
  )
  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema as never), defaultValues: { amount: 1, from: null, to: null } })

  // Start from the default currency into the first other one, once the list has arrived.
  const { from, to } = form.getValues()
  useEffect(() => {
    if (currencies.length === 0 || (from !== null && to !== null)) return
    const base = currencies.find((c) => c.isDefault) ?? currencies[0]!
    const other = currencies.find((c) => c.id !== base.id)
    form.reset({ amount: 1, from: base.id, to: other?.id ?? base.id })
  }, [currencies, from, to, form])

  const options = currencies.map((c) => ({ value: c.id, label: `${c.code} - ${c.name}` }))
  const submit = form.handleSubmit((values) => {
    convert.mutate({ amount: values.amount as number, fromCurrencyId: values.from as number, toCurrencyId: values.to as number })
  })

  return (
    <form className={styles.card} onSubmit={submit} noValidate aria-label={t('Converter')}>
      <h2 className={styles.cardTitle}>{t('Converter')}</h2>
      <p className={styles.note}>{t('ConverterHint')}</p>
      <div className={styles.cardRow}>
        <NumberField<FormValues> control={form.control} name="amount" decimals={2} min={0} label={t('Amount')} />
        <SelectField<FormValues, number> control={form.control} name="from" label={t('FromCurrency')} options={options} />
        <SelectField<FormValues, number> control={form.control} name="to" label={t('ToCurrency')} options={options} />
        <Button type="submit" variant="primary" loading={convert.isPending}>
          {t('Convert')}
        </Button>
      </div>
      {convert.data && (
        <p className={styles.result} role="status">
          {ltrIsolate(`${formatNumber(convert.data.amount, 2)} ${convert.data.fromCode} = ${formatNumber(convert.data.converted, 2)} ${convert.data.toCode}`)}
        </p>
      )}
      {convert.isError && <p className={styles.warn}>{convert.error.message}</p>}
    </form>
  )
}
