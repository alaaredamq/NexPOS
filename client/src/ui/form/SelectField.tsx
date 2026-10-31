import { useState } from 'react'
import { useController, type Control, type FieldValues, type Path } from 'react-hook-form'
import { Select, type SelectOption } from '../Select'
import { Field, type FieldChrome } from './Field'

interface SelectFieldProps<T extends FieldValues, V extends string | number> extends FieldChrome {
  name: Path<T>
  control?: Control<T>
  options?: SelectOption<V>[]
  loadOptions?: (query: string) => Promise<SelectOption<V>[]>
  /**
   * Label for the value the form starts with, when using `loadOptions` (edit screens): the async list
   * hasn't been fetched yet, so the field can't otherwise know what to display for id 42.
   */
  initialLabel?: string
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
}

/** Searchable select bound to react-hook-form. The form value is the option's `value` (or `null`). */
export function SelectField<T extends FieldValues, V extends string | number = string | number>({
  name,
  control,
  label,
  hint,
  required,
  options,
  loadOptions,
  initialLabel,
  placeholder,
  clearable,
  disabled,
}: SelectFieldProps<T, V>) {
  const { field, fieldState } = useController({ name, control })
  const [picked, setPicked] = useState<SelectOption<V> | null>(null)

  const current = field.value as V | null | undefined
  let selected: SelectOption<V> | null = null
  if (current !== null && current !== undefined && current !== '') {
    selected =
      options?.find((o) => o.value === current) ??
      (picked?.value === current ? picked : initialLabel ? { value: current, label: initialLabel } : null)
  }

  return (
    <Field label={label} hint={hint} required={required} error={fieldState.error?.message}>
      {(a11y) => (
        <Select<V>
          {...a11y}
          aria-invalid={fieldState.error ? true : undefined}
          invalid={!!fieldState.error}
          name={field.name}
          value={selected}
          options={options}
          loadOptions={loadOptions}
          placeholder={placeholder}
          clearable={clearable}
          disabled={disabled}
          onBlur={field.onBlur}
          onChange={(opt) => {
            setPicked(opt)
            field.onChange(opt ? opt.value : null)
          }}
        />
      )}
    </Field>
  )
}
