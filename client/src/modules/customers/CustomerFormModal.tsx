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
import { useSaveCustomer, type Customer } from './api'
import styles from './customers.module.css'

interface FormValues {
  firstName: string
  lastName: string
  email: string
  phoneNumber: string
  address: string
  isActive: boolean
}

interface Props {
  /** The customer being edited; omit to create one. */
  customer?: Pick<Customer, 'id' | 'firstName' | 'lastName' | 'email' | 'phoneNumber' | 'address' | 'isActive'>
  onClose: () => void
  /** Called with the saved customer, after the dialog's own toast. */
  onSaved?: (customer: Customer) => void
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/

/** Create / edit dialog. Only the first and last name are required; the balance is never edited here (it moves through the debt ledger). */
export function CustomerFormModal({ customer, onClose, onSaved }: Props) {
  const { t } = useTranslation(['customers', 'common'])
  const save = useSaveCustomer()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(() => {
    const name = z.string().refine((v) => v.trim().length >= 2 && v.trim().length <= 100, t('NameLengthError', { ns: 'customers' }))
    return z.object({
      firstName: name,
      lastName: name,
      email: z.string().max(255, t('EmailTooLong', { ns: 'customers' })).refine((v) => v.trim() === '' || EMAIL.test(v.trim()), t('EmailInvalid', { ns: 'customers' })),
      phoneNumber: z.string().max(20, t('PhoneTooLong', { ns: 'customers' })),
      address: z.string().max(500, t('AddressTooLong', { ns: 'customers' })),
      isActive: z.boolean(),
    })
  }, [t])

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema),
    defaultValues: {
      firstName: customer?.firstName ?? '',
      lastName: customer?.lastName ?? '',
      email: customer?.email ?? '',
      phoneNumber: customer?.phoneNumber ?? '',
      address: customer?.address ?? '',
      isActive: customer?.isActive ?? true,
    },
  })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      const saved = await save.mutateAsync({
        id: customer?.id,
        input: {
          firstName: values.firstName.trim(),
          lastName: values.lastName.trim(),
          email: values.email.trim() || null,
          phoneNumber: values.phoneNumber.trim() || null,
          address: values.address.trim() || null,
          isActive: values.isActive,
        },
      })
      toast.success(customer ? t('CustomerUpdated', { ns: 'customers' }) : t('CustomerCreated', { ns: 'customers' }))
      onSaved?.(saved)
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        // Deleted by someone else while the dialog was open.
        toast.error(t('CustomerNotFound', { ns: 'customers' }))
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  return (
    <Modal onClose={onClose} width={560} title={customer ? t('Cust_EditTitle', { ns: 'common' }) : t('AddCustomer', { ns: 'customers' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <div className={styles.formRow}>
              <TextField<FormValues> control={form.control} name="firstName" label={t('First Name', { ns: 'common' })} required autoFocus maxLength={100} autoComplete="off" />
              <TextField<FormValues> control={form.control} name="lastName" label={t('Last Name', { ns: 'common' })} required maxLength={100} autoComplete="off" />
            </div>
            <div className={styles.formRow}>
              <TextField<FormValues> control={form.control} name="email" type="email" dir="ltr" label={t('Email Address', { ns: 'common' })} maxLength={255} autoComplete="off" />
              <TextField<FormValues> control={form.control} name="phoneNumber" type="tel" dir="ltr" label={t('Phone Number', { ns: 'common' })} maxLength={20} autoComplete="off" />
            </div>
            <TextField<FormValues> control={form.control} name="address" label={t('Address', { ns: 'common' })} multiline rows={3} maxLength={500} />
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('Cust_IsActive', { ns: 'common' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {t('Save', { ns: 'common' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
