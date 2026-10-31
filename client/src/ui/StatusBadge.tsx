import styles from './StatusBadge.module.css'

interface StatusBadgeProps {
  label: string
  tone?: 'success' | 'neutral' | 'warning' | 'danger' | 'info' | 'ink'
  filled?: boolean
}

export function StatusBadge({ label, tone = 'neutral', filled = false }: StatusBadgeProps) {
  return (
    <span className={styles.badge} data-tone={tone} data-filled={filled}>
      {label}
    </span>
  )
}
