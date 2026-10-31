import { useController, type Control, type FieldValues, type Path } from 'react-hook-form'
import { Field, type FieldChrome } from './Field'
import { NumericInput } from './NumericInput'

interface MoneyFieldProps<T extends FieldValues> extends FieldChrome {
  name: Path<T>
  control?: Control<T>
  /** Currency notation shown at the start edge (`USD`, `LBP`). */
  currency?: string
  /** Decimal places for this currency (2 by default; LBP is usually 0). */
  decimals?: number
  /** Defaults to 0 — negative money is opt-in (refunds, adjustments). */
  min?: number
  max?: number
  placeholder?: string
  disabled?: boolean
  autoFocus?: boolean
  selectOnFocus?: boolean
}

/**
 * Money bound to react-hook-form. The form value is `number | null` in the currency's major unit,
 * rounded half-to-even and padded (`5` → `5.00`) on blur. The browser never decides a total — this is
 * for entering prices/amounts; totals come from the server.
 */
export function MoneyField<T extends FieldValues>({
  name,
  control,
  label,
  hint,
  required,
  currency,
  decimals = 2,
  min = 0,
  max,
  placeholder,
  disabled,
  autoFocus,
  selectOnFocus,
}: MoneyFieldProps<T>) {
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
          padDecimals
          min={min}
          max={max}
          prefix={currency}
          placeholder={placeholder}
          disabled={disabled}
          autoFocus={autoFocus}
          selectOnFocus={selectOnFocus}
        />
      )}
    </Field>
  )
}
