import { useController, type Control, type FieldValues, type Path } from 'react-hook-form'
import { Field, type FieldChrome } from './Field'
import styles from './field.module.css'

interface DateFieldProps<T extends FieldValues> extends FieldChrome {
  name: Path<T>
  control?: Control<T>
  /** `yyyy-mm-dd` bounds. */
  min?: string
  max?: string
  disabled?: boolean
}

/**
 * Date-only input bound to react-hook-form. The form value is a `yyyy-mm-dd` string (or `''`), which is
 * exactly what `<input type=date>` produces and what the API's DateOnly fields accept — no timezone maths.
 */
export function DateField<T extends FieldValues>({ name, control, label, hint, required, min, max, disabled }: DateFieldProps<T>) {
  const { field, fieldState } = useController({ name, control })
  return (
    <Field label={label} hint={hint} required={required} error={fieldState.error?.message}>
      {(a11y) => (
        <input {...field} {...a11y} value={field.value ?? ''} type="date" className={styles.control} min={min} max={max} disabled={disabled} />
      )}
    </Field>
  )
}
