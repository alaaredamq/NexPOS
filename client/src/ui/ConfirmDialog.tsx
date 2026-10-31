import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from './Button'
import { Modal, ModalBody, ModalFooter } from './Modal'
import styles from './ConfirmDialog.module.css'

interface ConfirmDialogProps {
  title: string
  /** What is about to happen, in the user's words. Keep it specific — name the record being destroyed. */
  message: string
  /** Extra consequence worth spelling out, e.g. that sale history is unaffected. */
  detail?: string
  confirmLabel: string
  cancelLabel?: string
  /** Styles the confirm button as destructive. */
  danger?: boolean
  onConfirm: () => void | Promise<void>
  onCancel: () => void
}

/** Yes/no gate for an irreversible action. Stays open while the confirm is in flight so a slow API call can't be double-fired. */
export function ConfirmDialog({ title, message, detail, confirmLabel, cancelLabel, danger, onConfirm, onCancel }: ConfirmDialogProps) {
  const { t } = useTranslation()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const handleConfirm = async () => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      await onConfirm()
    } catch (err) {
      // Leave the dialog up: the record wasn't destroyed, and closing would imply it was.
      setError(err instanceof Error ? err.message : t('common.actionFailed', 'Action failed'))
      setBusy(false)
    }
  }

  return (
    <Modal onClose={busy ? () => {} : onCancel} width={400} title={title} hideClose>
      <ModalBody>
        <div className={styles.body}>
          <div className={styles.message}>{message}</div>
          {detail && <div className={styles.detail}>{detail}</div>}
          {error && (
            <div className={styles.error} role="alert">
              {error}
            </div>
          )}
        </div>
      </ModalBody>
      <ModalFooter>
        <Button variant="ghost" disabled={busy} onClick={onCancel}>
          {cancelLabel ?? t('common.cancel', 'Cancel')}
        </Button>
        <Button variant={danger ? 'danger' : 'primary'} loading={busy} onClick={handleConfirm} data-autofocus={!danger || undefined}>
          {confirmLabel}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
