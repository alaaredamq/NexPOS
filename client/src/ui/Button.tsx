import type { ButtonHTMLAttributes } from 'react'
import { Loader } from './Loader'
import styles from './Button.module.css'

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
  size?: 'md' | 'lg'
  /** Shows a spinner, disables the button and sets aria-busy — keeps width so the layout doesn't jump. */
  loading?: boolean
  /** Stretch to the container's inline size. */
  block?: boolean
}

/** The one text button. 44 px tall by default (touch target); `lg` is for POS-style primary actions. */
export function Button({
  variant = 'secondary',
  size = 'md',
  loading = false,
  block = false,
  type = 'button',
  className,
  disabled,
  children,
  ...rest
}: ButtonProps) {
  return (
    <button
      type={type}
      className={className ? `${styles.btn} ${className}` : styles.btn}
      data-variant={variant}
      data-size={size}
      data-block={block || undefined}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...rest}
    >
      {loading && <Loader size="sm" tone={variant === 'secondary' || variant === 'ghost' ? 'primary' : 'inverse'} />}
      {children}
    </button>
  )
}
