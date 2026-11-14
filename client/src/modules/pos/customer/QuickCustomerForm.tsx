import { useId, useState, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { toast } from '@/ui/toast'
import { useQuickCreateCustomer, type PosCustomer } from '../checkout/api'
import styles from '../checkout/checkout.module.css'

interface Props {
  onCreated: (customer: PosCustomer) => void
  onCancel: () => void
}

const MIN = 2

/**
 * First and last name, nothing else (`POST pos/customers`): the old checkout's inline "new customer" row. Not a nested <form> (it lives
 * inside the payment form), so Enter is handled here and never submits the sale.
 */
export function QuickCustomerForm({ onCreated, onCancel }: Props) {
  const { t } = useTranslation('pos')
  const create = useQuickCreateCustomer()
  const firstId = useId()
  const lastId = useId()
  const phoneId = useId()
  const [first, setFirst] = useState('')
  const [last, setLast] = useState('')
  const [phone, setPhone] = useState('')
  const [error, setError] = useState<string | null>(null)

  const save = async () => {
    const firstName = first.trim()
    const lastName = last.trim()
    if (firstName.length < MIN || lastName.length < MIN) {
      setError(t('enterCustomerName'))
      return
    }
    setError(null)
    try {
      const phoneNumber = phone.trim()
      const customer = await create.mutateAsync(phoneNumber ? { firstName, lastName, phoneNumber } : { firstName, lastName })
      toast.success(t('customerAdded'))
      onCreated(customer)
    } catch (e) {
      setError(isApiError(e) && e.status !== 0 ? e.message : t('failedToCreateCustomer'))
    }
  }

  const onKeyDown = (event: KeyboardEvent) => {
    if (event.key !== 'Enter') return
    event.preventDefault()
    event.stopPropagation()
    void save()
  }

  return (
    <div className={styles.quickCustomer} role="group" aria-label={t('newCustomerTitle')} onKeyDown={onKeyDown}>
      <div className={styles.quickCustomerFields}>
        <div className={styles.fieldGroup}>
          <label htmlFor={firstId} className={styles.fieldLabel}>
            {t('firstName')}
          </label>
          <input id={firstId} className={styles.input} value={first} maxLength={100} onChange={(e) => setFirst(e.target.value)} autoFocus />
        </div>
        <div className={styles.fieldGroup}>
          <label htmlFor={lastId} className={styles.fieldLabel}>
            {t('lastName')}
          </label>
          <input id={lastId} className={styles.input} value={last} maxLength={100} onChange={(e) => setLast(e.target.value)} />
        </div>
        <div className={`${styles.fieldGroup} ${styles.quickCustomerWide}`}>
          <label htmlFor={phoneId} className={styles.fieldLabel}>
            {t('phoneOptional')}
          </label>
          <input
            id={phoneId}
            className={styles.input}
            type="tel"
            dir="ltr"
            value={phone}
            maxLength={20}
            autoComplete="off"
            onChange={(e) => setPhone(e.target.value)}
          />
        </div>
      </div>
      {error && (
        <p role="alert" className={styles.fieldError}>
          {error}
        </p>
      )}
      <div className={styles.quickCustomerActions}>
        <Button variant="ghost" onClick={onCancel} disabled={create.isPending}>
          {t('Cancel', { ns: 'common' })}
        </Button>
        <Button variant="primary" onClick={() => void save()} loading={create.isPending}>
          {t('saveAndSelect')}
        </Button>
      </div>
    </div>
  )
}
