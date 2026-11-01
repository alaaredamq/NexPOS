import { useTranslation } from 'react-i18next'
import { formatMoney } from '@/ui/format'
import styles from './customers.module.css'

interface Props {
  /** Positive: the customer owes the business. Negative: the business owes the customer. */
  amount: number
  notation: string
}

/** A balance in words and colour as well as sign (colour alone would not reach everyone): "owes me", "I owe", or a plain zero. */
export function Balance({ amount, notation }: Props) {
  const { t } = useTranslation('customers')
  if (amount === 0) return <span className={styles.muted}>{formatMoney(0, notation)}</span>
  return (
    <span className={styles.balance} data-direction={amount > 0 ? 'owes-me' : 'i-owe'} title={amount > 0 ? t('BalanceOwesMe') : t('BalanceIOwe')}>
      {formatMoney(amount, notation)}
    </span>
  )
}
