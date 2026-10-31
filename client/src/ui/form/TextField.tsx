import { useController, type Control, type FieldValues, type Path } from 'react-hook-form'
import { Field, type FieldChrome } from './Field'
import styles from './field.module.css'

interface TextFieldProps<T extends FieldValues> extends FieldChrome {
  name: Path<T>
  /** Optional: defaults to the surrounding `<FormProvider>`. */
  control?: Control<T>
  type?: 'text' | 'email' | 'tel' | 'password' | 'url' | 'search'
  placeholder?: string
  disabled?: boolean
  autoFocus?: boolean
  autoComplete?: string
  maxLength?: number
  /** Multi-line input. */
  multiline?: boolean
  rows?: number
  /** Force direction for values that are always LTR (emails, SKUs, phone numbers). */
  dir?: 'ltr' | 'rtl' | 'auto'
}

/** Text input / textarea bound to react-hook-form; shows the field's validation or server error inline. */
export function TextField<T extends FieldValues>({
  name,
  control,
  label,
  hint,
  required,
  type = 'text',
  placeholder,
  disabled,
  autoFocus,
  autoComplete,
  maxLength,
  multiline,
  rows = 3,
  dir,
}: TextFieldProps<T>) {
  const { field, fieldState } = useController({ name, control })
  const common = {
    ...field,
    value: field.value ?? '',
    className: styles.control,
    placeholder,
    disabled,
    autoFocus,
    autoComplete,
    maxLength,
    dir,
  }
  return (
    <Field label={label} hint={hint} required={required} error={fieldState.error?.message}>
      {(a11y) => (multiline ? <textarea {...common} {...a11y} rows={rows} /> : <input {...common} {...a11y} type={type} />)}
    </Field>
  )
}
