import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { useResetPin, type UserRow } from './api'
import styles from './users.module.css'

interface Props {
  user: Pick<UserRow, 'id' | 'fullName'>
  onClose: () => void
}

/** An admin sets a new PIN; the user has to replace it at their next sign-in. */
export function ResetPinModal({ user, onClose }: Props) {
  const { t } = useTranslation(['users', 'common'])
  const reset = useResetPin()
  const [pin, setPin] = useState('')
  const [errors, setErrors] = useState<string[]>([])
  const valid = /^\d{4}$/.test(pin)

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    if (!valid) {
      setErrors([t('PinRule', { ns: 'users' })])
      return
    }
    try {
      await reset.mutateAsync({ id: user.id, newPin: pin })
      toast.success(t('PinReset', { ns: 'users' }))
      onClose()
    } catch (error) {
      setErrors([isApiError(error) ? error.message : t('common.actionFailed', { ns: 'common' })])
    }
  }

  return (
    <Modal onClose={onClose} width={440} title={t('ResetPin', { ns: 'users' })}>
      <form onSubmit={(e) => void submit(e)} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <p className={styles.lead}>
              <bdi>{user.fullName}</bdi>
            </p>
            <FormError messages={errors} />
            <div className={styles.pinField}>
              <label htmlFor="reset-pin">{t('NewPinFourDigits', { ns: 'users' })}</label>
              <input
                id="reset-pin"
                className={styles.pinInput}
                dir="ltr"
                inputMode="numeric"
                autoComplete="off"
                maxLength={4}
                value={pin}
                data-autofocus
                onChange={(e) => setPin(e.target.value.replace(/\D/g, '').slice(0, 4))}
              />
              <small>{t('ResetPinHint', { ns: 'users' })}</small>
            </div>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={reset.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={reset.isPending} disabled={!valid}>
            {t('ResetPin', { ns: 'users' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
