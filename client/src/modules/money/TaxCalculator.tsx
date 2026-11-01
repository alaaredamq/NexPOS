import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { formatNumber, formatPercent } from '@/ui/format'
import { zodResolver } from '../_shared/zodResolver'
import { ALL, useCalculateTax, useTaxes } from './api'
import styles from './money.module.css'

interface FormValues {
  baseAmount: number | null
  taxId: number | null
}

/** "What does this tax add to an amount?" - asked of the server, so it counts only a tax that is in force today. Changes nothing. */
export function TaxCalculator() {
  const { t } = useTranslation('money')
  const taxes = useTaxes({ ...ALL, sort: 'name' }).data?.items ?? []
  const calculate = useCalculateTax()

  const schema = useMemo(
    () =>
      z.object({
        baseAmount: z.number({ error: t('AmountRequired') }).nullable().refine((v) => v !== null && v >= 0, t('AmountRequired')),
        taxId: z.number().nullable(),
      }),
    [t],
  )
  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema as never), defaultValues: { baseAmount: 100, taxId: null } })

  const submit = form.handleSubmit((values) => calculate.mutate({ baseAmount: values.baseAmount as number, taxId: values.taxId }))
  const result = calculate.data

  return (
    <form className={styles.card} onSubmit={submit} noValidate aria-label={t('TaxCalculator')}>
      <h2 className={styles.cardTitle}>{t('TaxCalculator')}</h2>
      <div className={styles.cardRow}>
        <NumberField<FormValues> control={form.control} name="baseAmount" decimals={2} min={0} label={t('BaseAmount')} />
        <SelectField<FormValues, number>
          control={form.control}
          name="taxId"
          label={t('TaxToApply')}
          options={taxes.map((x) => ({ value: x.id, label: `${x.name} (${formatPercent(x.rate, 2)})` }))}
          placeholder={t('DefaultTax')}
          clearable
        />
        <Button type="submit" variant="primary" loading={calculate.isPending}>
          {t('Calculate')}
        </Button>
      </div>
      {result && (
        <div className={styles.resultGrid} role="status">
          <div className={styles.resultCell}>
            <span className={styles.resultLabel}>{t('BaseAmount')}</span>
            <bdi className={styles.resultValue}>{formatNumber(result.baseAmount, 2)}</bdi>
          </div>
          <div className={styles.resultCell}>
            <span className={styles.resultLabel}>{t('TaxAmount')}</span>
            <bdi className={styles.resultValue}>{formatNumber(result.taxAmount, 2)}</bdi>
          </div>
          <div className={styles.resultCell}>
            <span className={styles.resultLabel}>{t('TotalAmount')}</span>
            <bdi className={styles.resultValue}>{formatNumber(result.totalAmount, 2)}</bdi>
          </div>
        </div>
      )}
      {calculate.isError && <p className={styles.warn}>{calculate.error.message}</p>}
    </form>
  )
}
