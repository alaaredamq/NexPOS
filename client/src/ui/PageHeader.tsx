import type { ReactNode } from 'react'
import { BackLink } from './BackLink'
import { Button } from './Button'
import styles from './PageHeader.module.css'

interface PageHeaderProps {
  title: string
  /** A link back to the list this page was opened from; shown above the title. */
  back?: { to: string; label: string }
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
  back,
  count,
  primaryActionLabel,
  onPrimaryAction,
  primaryActionDisabled = false,
  primaryActionLoading = false,
  children,
}: PageHeaderProps) {
  return (
    <div className={styles.header}>
      {back ? (
        <div className={styles.heading}>
          <BackLink to={back.to}>{back.label}</BackLink>
          <h1 className={styles.title}>{title}</h1>
        </div>
      ) : (
        <h1 className={styles.title}>{title}</h1>
      )}
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
