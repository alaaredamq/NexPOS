import { useCallback, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { Modal } from './Modal'
import { PinPad } from './PinPad'
import styles from './ManagerPinModal.module.css'

interface ManagerPinModalProps {
  /** Human description of what the PIN unlocks, e.g. "void this order". Shown in the subtitle. */
  action: string
  /**
   * Checks the PIN with the server (0D wires this to the session/auth API). Resolves true when a
   * manager PIN matches. Injected rather than imported so `ui/` stays independent of `core/auth`.
   */
  verify: (pin: string) => Promise<boolean>
  /**
   * Called with the verified PIN. Callers whose action is also gated server-side (opening or closing a
   * drawer) pass it along with the request so the server re-checks it; the rest can ignore it.
   */
  onSuccess: (pin: string) => void
  onCancel: () => void
  pinLength?: number
}

export function ManagerPinModal({ action, verify, onSuccess, onCancel, pinLength = 4 }: ManagerPinModalProps) {
  const { t } = useTranslation()
  const [pin, setPin] = useState('')
  const [shake, setShake] = useState(false)
  const [verifying, setVerifying] = useState(false)
  const pinRef = useRef('')

  const handleKey = useCallback(
    (key: string) => {
      if (verifying) return
      if (key === 'C') {
        pinRef.current = ''
        setPin('')
        return
      }
      if (key === '⌫') {
        pinRef.current = pinRef.current.slice(0, -1)
        setPin(pinRef.current)
        return
      }
      // The last digit submits on its own, so Enter has nothing left to do here — PinPad still
      // reports it so it can be swallowed rather than re-activating the button behind the modal.
      if (key === 'Enter' || pinRef.current.length >= pinLength) return
      const next = pinRef.current + key
      pinRef.current = next
      setPin(next)
      if (next.length === pinLength) {
        setVerifying(true)
        verify(next)
          .then((ok) => {
            if (ok) {
              onSuccess(next)
              return
            }
            setShake(true)
            setTimeout(() => {
              setShake(false)
              pinRef.current = ''
              setPin('')
            }, 320)
          })
          .catch(() => {
            setShake(true)
            setTimeout(() => {
              setShake(false)
              pinRef.current = ''
              setPin('')
            }, 320)
          })
          .finally(() => setVerifying(false))
      }
    },
    [verifying, pinLength, verify, onSuccess],
  )

  return (
    <Modal onClose={onCancel} width={320} title={t('common.managerPinTitle', 'Manager PIN')} hideClose>
      <div className={styles.body}>
        <div className={styles.subtitle}>{t('common.managerPinSubtitle', { action, defaultValue: 'Required to {{action}}' })}</div>
        <PinPad pin={pin} onKey={handleKey} shake={shake} dotCount={pinLength} />
        <div className={styles.error} role="alert">
          {shake ? t('common.managerPinError', 'Incorrect PIN') : ''}
        </div>
        <Button variant="secondary" onClick={onCancel}>
          {t('common.cancel', 'Cancel')}
        </Button>
      </div>
    </Modal>
  )
}
