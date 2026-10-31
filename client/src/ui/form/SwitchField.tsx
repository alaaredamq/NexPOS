import { useId } from 'react'
import { useController, type Control, type FieldValues, type Path } from 'react-hook-form'
import styles from './field.module.css'

interface SwitchFieldProps<T extends FieldValues> {
  name: Path<T>
  control?: Control<T>
  label: string
  hint?: string
  disabled?: boolean
}

/** On/off toggle bound to react-hook-form (boolean). `role="switch"`, Space/Enter toggles. */
export function SwitchField<T extends FieldValues>({ name, control, label, hint, disabled }: SwitchFieldProps<T>) {
  const { field } = useController({ name, control })
  const id = useId()
  const on = !!field.value

  return (
    <div className={styles.switchRow}>
      <button
        type="button"
        id={id}
        role="switch"
        aria-checked={on}
        aria-labelledby={`${id}-label`}
        aria-describedby={hint ? `${id}-hint` : undefined}
        className={styles.switch}
        disabled={disabled}
        onClick={() => field.onChange(!on)}
        onBlur={field.onBlur}
      />
      <div className={styles.switchText}>
        <label id={`${id}-label`} htmlFor={id} className={styles.label}>
          {label}
        </label>
        {hint && (
          <span id={`${id}-hint`} className={styles.hint}>
            {hint}
          </span>
        )}
      </div>
    </div>
  )
}
