import type { ButtonHTMLAttributes } from 'react'
import { Icon, type IconName } from './Icon'
import styles from './IconButton.module.css'

interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children' | 'aria-label' | 'title'> {
  icon: IconName
  /** The icon stands in for a visible label, so this is required — it becomes the tooltip and the a11y name. */
  label: string
  tone?: 'default' | 'primary' | 'danger'
  size?: 'sm' | 'md'
  /** Armed/pressed look, for two-step actions like confirm-delete. */
  active?: boolean
}

/**
 * A square action button for table rows. Rows carry up to four of these, and spelled-out labels
 * crowded the columns they sat next to — the label lives in the tooltip and the accessible name.
 */
export function IconButton({ icon, label, tone = 'default', size = 'md', active, className, ...rest }: IconButtonProps) {
  return (
    <button
      type="button"
      className={className ? `${styles.btn} ${className}` : styles.btn}
      data-tone={tone}
      data-size={size}
      data-active={active || undefined}
      title={label}
      aria-label={label}
      {...rest}
    >
      <Icon name={icon} size={size === 'sm' ? 15 : 16} />
    </button>
  )
}
