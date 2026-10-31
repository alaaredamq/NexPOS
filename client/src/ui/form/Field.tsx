import { useId, type ReactNode } from 'react'
import styles from './field.module.css'

export interface FieldChrome {
  label?: string
  hint?: string
  required?: boolean
  /** Message to show under the input (validation or server error). */
  error?: string
}

export interface FieldA11y {
  id: string
  'aria-describedby': string | undefined
  'aria-invalid': true | undefined
  'aria-required': true | undefined
}

interface FieldProps extends FieldChrome {
  children: (a11y: FieldA11y) => ReactNode
}

/**
 * Label + control + hint + error with the aria wiring done once: the label targets the control's id,
 * hint and error are linked through aria-describedby, and the error is announced (role=alert).
 */
export function Field({ label, hint, required, error, children }: FieldProps) {
  const id = useId()
  const hintId = hint ? `${id}-hint` : undefined
  const errorId = error ? `${id}-error` : undefined
  const describedBy = [errorId, hintId].filter(Boolean).join(' ') || undefined

  return (
    <div className={styles.field}>
      {label && (
        <label htmlFor={id} className={styles.label}>
          {label}
          {required && (
            <span className={styles.required} aria-hidden="true">
              *
            </span>
          )}
        </label>
      )}
      {children({ id, 'aria-describedby': describedBy, 'aria-invalid': error ? true : undefined, 'aria-required': required ? true : undefined })}
      {hint && !error && (
        <div id={hintId} className={styles.hint}>
          {hint}
        </div>
      )}
      {error && (
        <div id={errorId} className={styles.error} role="alert">
          {error}
        </div>
      )}
    </div>
  )
}
