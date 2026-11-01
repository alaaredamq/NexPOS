import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { DateField } from '@/ui/form/DateField'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSaveTax, useTaxCategories, type TaxRate } from './api'
import styles from './money.module.css'
import { dayOf, endOfDay, startOfDay } from './taxDates'
import { useFormRunner } from './useFormRunner'

interface FormValues {
  name: string
  rate: number | null
  type: number | null
  taxCode: string
  description: string
  effectiveFrom: string
  effectiveTo: string
  categoryId: number | null
  isDefault: boolean
  isActive: boolean
  isInclusive: boolean
  applyToAllProducts: boolean
}

interface Props {
  /** The tax being edited; omit to create one. */
  tax?: TaxRate
  onClose: () => void
}

/** Tax types in the order of the API's `type` (0 VAT ... 4 custom). */
export const TAX_TYPES = [
  { value: 0, key: 'TaxTypeVat' },
  { value: 1, key: 'TaxTypeSales' },
  { value: 2, key: 'TaxTypeService' },
  { value: 3, key: 'TaxTypeExcise' },
  { value: 4, key: 'TaxTypeCustom' },
] as const

/** Create / edit dialog for a tax rate. Making it the default takes the flag from the previous default. */
export function TaxFormModal({ tax, onClose }: Props) {
  const { t } = useTranslation(['money', 'common'])
  const save = useSaveTax()
  const categories = useTaxCategories().data ?? []

  const schema = useMemo(
    () =>
      z
        .object({
          name: z.string().refine((v) => v.trim().length > 0, t('NameRequired', { ns: 'money' })).refine((v) => v.trim().length <= 100, t('NameTooLong', { ns: 'money' })),
          rate: z.number({ error: t('PercentRange', { ns: 'money' }) }).nullable().refine((v) => v !== null && v >= 0 && v <= 100, t('PercentRange', { ns: 'money' })),
          type: z.number({ error: t('PickType', { ns: 'money' }) }).nullable().refine((v) => v !== null, t('PickType', { ns: 'money' })),
          taxCode: z.string().max(50, t('CodeTooLong', { ns: 'money' })),
          description: z.string().max(500, t('DescriptionTooLong', { ns: 'money' })),
          effectiveFrom: z.string(),
          effectiveTo: z.string(),
          categoryId: z.number().nullable(),
          isDefault: z.boolean(),
          isActive: z.boolean(),
          isInclusive: z.boolean(),
          applyToAllProducts: z.boolean(),
        })
        .refine((v) => !v.effectiveFrom || !v.effectiveTo || v.effectiveTo >= v.effectiveFrom, { path: ['effectiveTo'], message: t('DatesOrder', { ns: 'money' }) }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: {
      name: tax?.name ?? '',
      rate: tax?.rate ?? null,
      type: tax?.type ?? 0,
      taxCode: tax?.taxCode ?? '',
      description: tax?.description ?? '',
      effectiveFrom: dayOf(tax?.effectiveFrom),
      effectiveTo: dayOf(tax?.effectiveTo),
      categoryId: tax?.categoryId ?? null,
      isDefault: tax?.isDefault ?? false,
      isActive: tax?.isActive ?? true,
      isInclusive: tax?.isInclusive ?? false,
      applyToAllProducts: tax?.applyToAllProducts ?? true,
    },
  })
  const { formErrors, run } = useFormRunner(form, t('common.actionFailed', { ns: 'common' }), () => {
    toast.error(t('TaxNotFound', { ns: 'money' }))
    onClose()
  })

  const submit = form.handleSubmit((values) =>
    run(async () => {
      await save.mutateAsync({
        id: tax?.id,
        input: {
          name: values.name.trim(),
          rate: values.rate as number,
          type: values.type as number,
          taxCode: values.taxCode.trim() || null,
          description: values.description.trim() || null,
          effectiveFrom: startOfDay(values.effectiveFrom),
          effectiveTo: endOfDay(values.effectiveTo),
          categoryId: values.categoryId,
          isDefault: values.isDefault,
          isActive: values.isActive,
          isInclusive: values.isInclusive,
          applyToAllProducts: values.applyToAllProducts,
        },
      })
      toast.success(tax ? t('TaxUpdated', { ns: 'money' }) : t('TaxCreated', { ns: 'money' }))
      onClose()
    }),
  )

  return (
    <Modal onClose={onClose} width={720} title={tax ? t('EditTax', { ns: 'money' }) : t('AddTax', { ns: 'money' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <div className={styles.formRow}>
              <TextField<FormValues> control={form.control} name="name" label={t('TaxName', { ns: 'money' })} required maxLength={100} autoFocus autoComplete="off" />
              <NumberField<FormValues> control={form.control} name="rate" decimals={2} min={0} max={100} unit="%" label={t('TaxRateLabel', { ns: 'money' })} required />
            </div>
            <div className={styles.formRow}>
              <SelectField<FormValues, number>
                control={form.control}
                name="type"
                label={t('Type', { ns: 'common' })}
                required
                options={TAX_TYPES.map((x) => ({ value: x.value, label: t(x.key, { ns: 'money' }) }))}
              />
              <TextField<FormValues> control={form.control} name="taxCode" dir="ltr" label={t('TaxCode', { ns: 'money' })} maxLength={50} autoComplete="off" />
            </div>
            <TextField<FormValues> control={form.control} name="description" label={t('Description', { ns: 'common' })} multiline rows={2} maxLength={500} />
            <div className={styles.formRow}>
              <DateField<FormValues> control={form.control} name="effectiveFrom" label={t('EffectiveFrom', { ns: 'money' })} hint={t('EffectiveFromHint', { ns: 'money' })} />
              <DateField<FormValues> control={form.control} name="effectiveTo" label={t('EffectiveTo', { ns: 'money' })} hint={t('EffectiveToHint', { ns: 'money' })} />
            </div>
            <SelectField<FormValues, number>
              control={form.control}
              name="categoryId"
              label={t('CategoryOptional', { ns: 'money' })}
              options={categories.map((c) => ({ value: c.id, label: c.name }))}
              placeholder={t('AllCategories', { ns: 'money' })}
              clearable
            />
            <SwitchField<FormValues> control={form.control} name="applyToAllProducts" label={t('ApplyToAllProducts', { ns: 'money' })} />
            <SwitchField<FormValues> control={form.control} name="isInclusive" label={t('TaxInclusive', { ns: 'money' })} hint={t('TaxInclusiveHint', { ns: 'money' })} />
            <SwitchField<FormValues> control={form.control} name="isDefault" label={t('DefaultTax', { ns: 'money' })} hint={t('DefaultTaxHint', { ns: 'money' })} />
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('Active', { ns: 'common' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {tax ? t('Save', { ns: 'common' }) : t('AddTax', { ns: 'money' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
