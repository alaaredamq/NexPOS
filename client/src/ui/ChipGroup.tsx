import styles from './ChipGroup.module.css'

export interface ChipOption {
  value: string
  label: string
}

interface ChipGroupProps {
  options: ChipOption[]
  value: string
  onChange: (value: string) => void
  size?: 'sm' | 'md' | 'lg'
  'aria-label'?: string
}

/** Single-select pill filter (category chips, period tabs). */
export function ChipGroup({ options, value, onChange, size = 'md', 'aria-label': ariaLabel }: ChipGroupProps) {
  return (
    <div className={styles.group} role="group" aria-label={ariaLabel}>
      {options.map((opt) => (
        <button
          key={opt.value}
          type="button"
          className={styles.chip}
          data-size={size}
          data-active={opt.value === value}
          aria-pressed={opt.value === value}
          onClick={() => onChange(opt.value)}
        >
          {opt.label}
        </button>
      ))}
    </div>
  )
}

interface MultiChipGroupProps {
  options: ChipOption[]
  /** Currently selected values. */
  value: string[]
  onChange: (value: string[]) => void
  size?: 'sm' | 'md' | 'lg'
  'aria-label'?: string
}

/** Like ChipGroup, but chips toggle independently so several can be active at once. */
export function MultiChipGroup({ options, value, onChange, size = 'md', 'aria-label': ariaLabel }: MultiChipGroupProps) {
  const toggle = (val: string) => onChange(value.includes(val) ? value.filter((v) => v !== val) : [...value, val])

  return (
    <div className={styles.group} role="group" aria-label={ariaLabel}>
      {options.map((opt) => (
        <button
          key={opt.value}
          type="button"
          className={styles.chip}
          data-size={size}
          data-active={value.includes(opt.value)}
          aria-pressed={value.includes(opt.value)}
          onClick={() => toggle(opt.value)}
        >
          {opt.label}
        </button>
      ))}
    </div>
  )
}
