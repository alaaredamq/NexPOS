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
  return <Switch checked={!!field.value} onChange={field.onChange} onBlur={field.onBlur} label={label} hint={hint} disabled={disabled} />
}

interface SwitchProps {
  checked: boolean
  onChange: (checked: boolean) => void
  onBlur?: () => void
  label: string
  hint?: string
  disabled?: boolean
}

/** The same toggle without a form, for settings that apply the moment they are flipped. */
export function Switch({ checked: on, onChange, onBlur, label, hint, disabled }: SwitchProps) {
  const id = useId()

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
        onClick={() => onChange(!on)}
        onBlur={onBlur}
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
