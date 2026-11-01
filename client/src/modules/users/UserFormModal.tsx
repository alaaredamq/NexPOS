import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { SelectField } from '@/ui/form/SelectField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useRoleOptions, useSaveUser, type UserRow } from './api'
import styles from './users.module.css'

interface FormValues {
  username: string
  email: string
  firstName: string
  lastName: string
  pin: string
  roleId: number | null
  isActive: boolean
}

interface Props {
  /** The user being edited; omit to create one. */
  user?: UserRow
  onClose: () => void
  /** Editing only: shows a "Reset PIN" button that hands over to the reset dialog. */
  onResetPin?: () => void
}

const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/
const PIN = /^\d{4}$/

/** Create / edit dialog. Creating sets the account's PIN (the user must change it at first sign-in); editing keeps the username and PIN. */
export function UserFormModal({ user, onClose, onResetPin }: Props) {
  const { t } = useTranslation(['users', 'common'])
  const save = useSaveUser()
  const roles = useRoleOptions()
  const [formErrors, setFormErrors] = useState<string[]>([])
  const creating = !user

  const schema = useMemo(
    () =>
      z.object({
        username: creating ? z.string().refine((v) => v.trim().length > 0, t('UsernameRequired', { ns: 'users' })) : z.string(),
        email: z.string().refine((v) => EMAIL.test(v.trim()), t('EmailInvalid', { ns: 'users' })),
        firstName: z.string().max(100, t('NameTooLong', { ns: 'users' })),
        lastName: z.string().max(100, t('NameTooLong', { ns: 'users' })),
        pin: creating ? z.string().regex(PIN, t('PinRule', { ns: 'users' })) : z.string(),
        roleId: z.number({ error: t('RoleRequired', { ns: 'users' }) }).nullable().refine((v) => v !== null, t('RoleRequired', { ns: 'users' })),
        isActive: z.boolean(),
      }),
    [t, creating],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: {
      username: user?.username ?? '',
      email: user?.email ?? '',
      firstName: user?.firstName ?? '',
      lastName: user?.lastName ?? '',
      pin: '',
      roleId: user?.roleId ?? null,
      isActive: user?.isActive ?? true,
    },
  })

  const options = (roles.data?.items ?? []).map((r) => ({ value: r.id, label: r.name }))

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    const shared = {
      email: values.email.trim(),
      firstName: values.firstName.trim() || null,
      lastName: values.lastName.trim() || null,
      roleId: values.roleId as number,
      isActive: values.isActive,
    }
    try {
      await save.mutateAsync(
        user ? { id: user.id, input: shared } : { input: { ...shared, username: values.username.trim(), pin: values.pin } },
      )
      toast.success(user ? t('UserUpdated', { ns: 'users' }) : t('UserCreated', { ns: 'users' }))
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        toast.error(t('UserNotFound', { ns: 'users' }))
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  return (
    <Modal onClose={onClose} width={620} title={user ? t('EditUser', { ns: 'users' }) : t('AddUser', { ns: 'users' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <div className={styles.formRow}>
              <TextField<FormValues> control={form.control} name="username" dir="ltr" label={t('Username', { ns: 'common' })} required={creating} disabled={!creating} autoFocus={creating} maxLength={100} autoComplete="off" />
              <TextField<FormValues> control={form.control} name="email" type="email" dir="ltr" label={t('Email Address', { ns: 'common' })} required maxLength={255} autoComplete="off" autoFocus={!creating} />
            </div>
            <div className={styles.formRow}>
              <TextField<FormValues> control={form.control} name="firstName" label={t('First Name', { ns: 'common' })} maxLength={100} autoComplete="off" />
              <TextField<FormValues> control={form.control} name="lastName" label={t('Last Name', { ns: 'common' })} maxLength={100} autoComplete="off" />
            </div>
            <div className={styles.formRow}>
              {creating && (
                <TextField<FormValues> control={form.control} name="pin" dir="ltr" label={t('PinFourDigits', { ns: 'users' })} hint={t('PinHint', { ns: 'users' })} required maxLength={4} autoComplete="off" />
              )}
              <SelectField<FormValues, number> control={form.control} name="roleId" label={t('Role', { ns: 'users' })} required options={options} placeholder={t('SelectRole', { ns: 'users' })} />
            </div>
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('ActiveUser', { ns: 'users' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          {onResetPin && (
            <Button variant="ghost" className={styles.footerStart} onClick={onResetPin} disabled={save.isPending}>
              {t('ResetPin', { ns: 'users' })}
            </Button>
          )}
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {user ? t('Save', { ns: 'common' }) : t('CreateUser', { ns: 'users' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
