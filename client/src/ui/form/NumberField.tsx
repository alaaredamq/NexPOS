import { useController, type Control, type FieldValues, type Path } from 'react-hook-form'
import { Field, type FieldChrome } from './Field'
import { NumericInput } from './NumericInput'

interface NumberFieldProps<T extends FieldValues> extends FieldChrome {
  name: Path<T>
  control?: Control<T>
  /** Fractional digits allowed (default 3 for quantities; use 0 for counts). */
  decimals?: number
  min?: number
  max?: number
  /** Unit shown inside the input, e.g. `kg`, `pcs`. */
  unit?: string
  placeholder?: string
  disabled?: boolean
  autoFocus?: boolean
  selectOnFocus?: boolean
}

/** Quantity / generic number bound to react-hook-form. The form value is `number | null`. */
export function NumberField<T extends FieldValues>({
  name,
  control,
  label,
  hint,
  required,
  decimals = 3,
  min,
  max,
  unit,
  placeholder,
  disabled,
  autoFocus,
  selectOnFocus,
}: NumberFieldProps<T>) {
  const { field, fieldState } = useController({ name, control })
  return (
    <Field label={label} hint={hint} required={required} error={fieldState.error?.message}>
      {(a11y) => (
        <NumericInput
          {...a11y}
          name={field.name}
          value={(field.value as number | null | undefined) ?? null}
          onChange={field.onChange}
          onBlur={field.onBlur}
          decimals={decimals}
          min={min}
          max={max}
          suffix={unit}
          placeholder={placeholder}
          disabled={disabled}
          autoFocus={autoFocus}
          selectOnFocus={selectOnFocus}
        />
      )}
    </Field>
  )
}
