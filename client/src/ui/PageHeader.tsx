import type { ReactNode } from 'react'
import { Button } from './Button'
import styles from './PageHeader.module.css'

interface PageHeaderProps {
  title: string
  count?: string
  primaryActionLabel?: string
  onPrimaryAction?: () => void
  primaryActionDisabled?: boolean
  primaryActionLoading?: boolean
  /** Extra controls (filters, secondary buttons) placed before the primary action. */
  children?: ReactNode
}

export function PageHeader({
  title,
  count,
  primaryActionLabel,
  onPrimaryAction,
  primaryActionDisabled = false,
  primaryActionLoading = false,
  children,
}: PageHeaderProps) {
  return (
    <div className={styles.header}>
      <h1 className={styles.title}>{title}</h1>
      {count && <span className={styles.count}>{count}</span>}
      <div className={styles.spacer} />
      {children}
      {primaryActionLabel && onPrimaryAction && (
        <Button variant="primary" onClick={onPrimaryAction} disabled={primaryActionDisabled} loading={primaryActionLoading}>
          {primaryActionLabel}
        </Button>
      )}
    </div>
  )
}
