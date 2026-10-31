import { useTranslation } from 'react-i18next'
import { formatQty } from './format'
import styles from './Stepper.module.css'

interface StepperProps {
  qty: number
  onDec: () => void
  onInc: () => void
  /** Disable the decrement at the lower bound (e.g. qty 1). */
  decDisabled?: boolean
  incDisabled?: boolean
}

export function Stepper({ qty, onDec, onInc, decDisabled, incDisabled }: StepperProps) {
  const { t } = useTranslation()
  return (
    <div className={styles.stepper} dir="ltr">
      <button type="button" className={styles.btn} onClick={onDec} disabled={decDisabled} aria-label={t('common.decreaseQty', 'Decrease quantity')}>
        −
      </button>
      <div className={styles.qty} aria-live="polite">
        {formatQty(qty)}
      </div>
      <button type="button" className={styles.btn} onClick={onInc} disabled={incDisabled} aria-label={t('common.increaseQty', 'Increase quantity')}>
        +
      </button>
    </div>
  )
}
