import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { resetCart, useCartStore, type PosCartInput } from '../store/cartStore'
import { useHoldCart } from './api'
import styles from './cart.module.css'

interface Props {
  /** The cart as it would be sold. */
  input: PosCartInput
  onClose: () => void
}

/** Parks the cart so the next customer can be served. The customer (if one is attached) is whoever checkout's picker put on the cart. */
export function HoldDialog({ input, onClose }: Props) {
  const { t } = useTranslation('pos')
  const hold = useHoldCart()
  const customerId = useCartStore((s) => s.customerId)
  const [notes, setNotes] = useState(useCartStore.getState().notes)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    try {
      await hold.mutateAsync({ cart: input, customerId, notes: notes.trim() || null })
      resetCart() // parked: the till is free for the next customer
      toast.success(t('cartHeldSuccessfully'))
      onClose()
    } catch (e) {
      // The server checks a parked cart like a sale (a price below cost, an unknown product …): say why it was refused.
      setError(isApiError(e) ? e.message : t('failedToHoldCart'))
    }
  }

  return (
    <Modal onClose={onClose} width={420} title={t('holdCart')}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.dialogForm}>
            {error && (
              <div role="alert" className={styles.notice} data-tone="error">
                {error}
              </div>
            )}
            <div className={styles.fieldGroup}>
              <label htmlFor="hold-notes" className={styles.fieldLabel}>
                {t('notesOptional')}
              </label>
              <textarea
                id="hold-notes"
                className={styles.textarea}
                rows={3}
                maxLength={500}
                placeholder={t('addNotes')}
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                data-autofocus
              />
            </div>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={hold.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={hold.isPending}>
            {t('holdCart')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
