import { useTranslation } from 'react-i18next'
import { posBus, type PosCommand } from '../slots'
import styles from './layout.module.css'

interface QuickActionsProps {
  onPriceCheck: () => void
}

interface Action {
  id: string
  label: string
  hint: string
  keys: string
  run: () => void
}

/** The four buttons under the product grid. Price check is the catalog's own; the others are commands the cart (P3) answers. */
export function QuickActions({ onPriceCheck }: QuickActionsProps) {
  const { t } = useTranslation('pos')
  const command = (name: PosCommand) => () => void posBus.run(name)

  const actions: Action[] = [
    { id: 'retrieve', label: t('Retrieve'), hint: t('HeldCart'), keys: 'F4', run: command('retrieve') },
    { id: 'manual', label: t('AddItem'), hint: t('Manual'), keys: 'F7', run: command('manualItem') },
    { id: 'discount', label: t('Discount'), hint: t('Apply'), keys: 'F8', run: command('discount') },
    { id: 'price-check', label: t('PriceCheck'), hint: t('Check'), keys: 'F3', run: onPriceCheck },
  ]

  return (
    <div className={styles.quickActions}>
      {actions.map((action) => (
        <button key={action.id} type="button" className={styles.quickAction} onClick={action.run} data-testid={`quick-${action.id}`}>
          <span className={styles.quickLabel}>{action.label}</span>
          <span className={styles.quickHint}>{action.hint}</span>
          <kbd className={styles.kbd}>{action.keys}</kbd>
        </button>
      ))}
    </div>
  )
}
