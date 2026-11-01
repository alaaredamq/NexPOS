import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSaveSupplier, useSupplierOptions, type Supplier } from './api'
import styles from './suppliers.module.css'

interface FormValues {
  name: string
  email: string
  phone: string
  address: string
  city: string
  country: string
  postalCode: string
  currencyId: number | null
  discountRate: number | null
  isActive: boolean
}

interface Props {
  /** The supplier being edited; omit to create one. */
  supplier?: Supplier
  onClose: () => void
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Create / edit dialog. The balance is not a field: it only moves through the debt ledger. */
export function SupplierFormModal({ supplier, onClose }: Props) {
  const { t } = useTranslation(['suppliers', 'common'])
  const options = useSupplierOptions()
  const save = useSaveSupplier()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().refine((v) => v.trim().length >= 2 && v.trim().length <= 200, t('NameLengthError', { ns: 'suppliers' })),
        email: z.string().refine((v) => v.trim() === '' || EMAIL.test(v.trim()), t('EmailInvalid', { ns: 'suppliers' })),
        phone: z.string().max(20, t('PhoneTooLong', { ns: 'suppliers' })),
        address: z.string().max(500),
        city: z.string().max(100),
        country: z.string().max(100),
        postalCode: z.string().max(20),
        currencyId: z.number({ message: t('CurrencyRequired', { ns: 'suppliers' }) }),
        discountRate: z.number({ message: t('DiscountRange', { ns: 'suppliers' }) }).min(0, t('DiscountRange', { ns: 'suppliers' })).max(100, t('DiscountRange', { ns: 'suppliers' })),
        isActive: z.boolean(),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema),
    defaultValues: {
      name: supplier?.name ?? '',
      email: supplier?.email ?? '',
      phone: supplier?.phone ?? '',
      address: supplier?.address ?? '',
      city: supplier?.city ?? '',
      country: supplier?.country ?? '',
      postalCode: supplier?.postalCode ?? '',
      currencyId: supplier?.currencyId ?? null,
      discountRate: supplier?.discountRate ?? 0,
      isActive: supplier?.isActive ?? true,
    },
  })

  // A new supplier starts in the shop's default currency, once the options have arrived (unless the user already picked one).
  const defaultCurrencyId = options.data?.defaultCurrencyId
  const { getValues, setValue } = form
  useEffect(() => {
    if (!supplier && defaultCurrencyId && getValues('currencyId') === null) setValue('currencyId', defaultCurrencyId)
  }, [supplier, defaultCurrencyId, getValues, setValue])

  const blank = (value: string) => value.trim() || null

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await save.mutateAsync({
        id: supplier?.id,
        input: {
          name: values.name.trim(),
          email: blank(values.email),
          phone: blank(values.phone),
          address: blank(values.address),
          city: blank(values.city),
          country: blank(values.country),
          postalCode: blank(values.postalCode),
          currencyId: values.currencyId ?? 0,
          discountRate: values.discountRate ?? 0,
          isActive: values.isActive,
        },
      })
      toast.success(supplier ? t('SupplierUpdated', { ns: 'suppliers' }) : t('SupplierCreated', { ns: 'suppliers' }))
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        toast.error(t('SupplierNotFound', { ns: 'suppliers' }))
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  const currencies = (options.data?.currencies ?? []).map((c) => ({ value: c.id, label: `${c.name} (${c.notation})` }))

  return (
    <Modal onClose={onClose} width={680} title={supplier ? t('EditSupplier', { ns: 'suppliers' }) : t('AddSupplier', { ns: 'suppliers' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          {options.isPending ? (
            <Loader fullSection label={t('Loading', { ns: 'common' })} />
          ) : (
            <div className={styles.form}>
              <FormError messages={formErrors} />
              <div className={styles.formGrid}>
                <div className={styles.formWide}>
                  <TextField<FormValues> control={form.control} name="name" label={t('SupplierName', { ns: 'suppliers' })} required autoFocus maxLength={200} placeholder={t('EnterSupplierName', { ns: 'suppliers' })} />
                </div>
                <TextField<FormValues> control={form.control} name="email" label={t('EmailAddress', { ns: 'suppliers' })} type="email" dir="ltr" maxLength={255} placeholder={t('EnterEmailAddress', { ns: 'suppliers' })} />
                <TextField<FormValues> control={form.control} name="phone" label={t('PhoneNumber', { ns: 'suppliers' })} type="tel" dir="ltr" maxLength={20} placeholder={t('EnterPhoneNumber', { ns: 'suppliers' })} />
                <div className={styles.formWide}>
                  <TextField<FormValues> control={form.control} name="address" label={t('Address', { ns: 'suppliers' })} maxLength={500} placeholder={t('EnterFullAddress', { ns: 'suppliers' })} />
                </div>
                <TextField<FormValues> control={form.control} name="city" label={t('City', { ns: 'suppliers' })} maxLength={100} />
                <TextField<FormValues> control={form.control} name="country" label={t('Country', { ns: 'suppliers' })} maxLength={100} />
                <TextField<FormValues> control={form.control} name="postalCode" label={t('PostalCode', { ns: 'suppliers' })} maxLength={20} dir="ltr" />
                <NumberField<FormValues> control={form.control} name="discountRate" label={t('DiscountRate', { ns: 'suppliers' })} decimals={2} min={0} max={100} unit="%" selectOnFocus />
                <div className={styles.formWide}>
                  <SelectField<FormValues, number> control={form.control} name="currencyId" label={t('Currency', { ns: 'suppliers' })} required options={currencies} placeholder={t('SelectCurrency', { ns: 'suppliers' })} />
                </div>
                <div className={styles.formWide}>
                  <SwitchField<FormValues> control={form.control} name="isActive" label={t('SupplierIsActive', { ns: 'suppliers' })} />
                </div>
              </div>
            </div>
          )}
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending} disabled={options.isPending}>
            {t('Save', { ns: 'common' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
