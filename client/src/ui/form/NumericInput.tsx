import { useEffect, useRef, useState, type FocusEvent } from 'react'
import { parseNumberInput, roundHalfEven } from '../format'
import styles from './field.module.css'

export interface NumericInputProps {
  value: number | null
  onChange: (value: number | null) => void
  /** Max fractional digits: 2 for money, 3 for quantities/weights, 0 for counts. */
  decimals?: number
  /** Pad to exactly `decimals` places on blur (money: `5` → `5.00`). Quantities leave it off. */
  padDecimals?: boolean
  min?: number
  max?: number
  /** Small label inside the start edge (currency code). */
  prefix?: string
  /** Small label inside the end edge (unit). */
  suffix?: string
  id?: string
  name?: string
  placeholder?: string
  disabled?: boolean
  autoFocus?: boolean
  onBlur?: () => void
  'aria-describedby'?: string
  'aria-invalid'?: boolean
  'aria-required'?: boolean
  'aria-label'?: string
  /** Select the whole value on focus (fast overwrite in POS tender / qty prompts). */
  selectOnFocus?: boolean
}

function toText(value: number | null, decimals: number, pad: boolean): string {
  if (value === null || Number.isNaN(value)) return ''
  const rounded = roundHalfEven(value, decimals)
  return pad ? rounded.toFixed(decimals) : String(rounded)
}

/**
 * Number input that never shows `1e-7`, groups nothing while typing, accepts Arabic-Indic digits and
 * `٫`, refuses more fractional digits than `decimals`, and rounds half-to-even like the C# services.
 * Emits `null` for empty. Always LTR and end-aligned (see field.module.css `.numeric`).
 */
export function NumericInput({
  value,
  onChange,
  decimals = 2,
  padDecimals = false,
  min,
  max,
  prefix,
  suffix,
  onBlur,
  selectOnFocus,
  ...rest
}: NumericInputProps) {
  const [text, setText] = useState(() => toText(value, decimals, padDecimals))
  const focused = useRef(false)

  // Follow outside changes (form reset, server value) but never fight the user's in-progress typing.
  useEffect(() => {
    if (!focused.current || parseNumberInput(text) !== value) setText(toText(value, decimals, padDecimals))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value])

  const handleChange = (raw: string) => {
    const parsed = parseNumberInput(raw)
    const looksPartial = raw.trim() === '' || raw.trim() === '-' || raw.trim() === '.' || raw.trim() === '-.'
    if (!looksPartial && parsed === null) return // not a number: swallow the keystroke
    if (parsed !== null && min !== undefined && min >= 0 && raw.includes('-')) return
    // Reject extra fractional digits instead of silently rounding under the cursor.
    const frac = raw.replace(/٫/g, '.').split('.')[1]
    if (frac !== undefined && frac.length > decimals) return
    if (raw.includes('.') && decimals === 0) return
    setText(raw)
    onChange(looksPartial ? null : parsed)
  }

  const handleBlur = (e: FocusEvent<HTMLInputElement>) => {
    focused.current = false
    void e
    let next = parseNumberInput(text)
    if (next !== null) {
      if (min !== undefined) next = Math.max(min, next)
      if (max !== undefined) next = Math.min(max, next)
      next = roundHalfEven(next, decimals)
      if (next !== value) onChange(next)
    }
    setText(toText(next, decimals, padDecimals))
    onBlur?.()
  }

  const input = (
    <input
      {...rest}
      type="text"
      dir="ltr"
      inputMode={decimals > 0 ? 'decimal' : 'numeric'}
      autoComplete="off"
      className={`${styles.control} ${styles.numeric}`}
      data-prefix={prefix ? '' : undefined}
      data-suffix={suffix ? '' : undefined}
      value={text}
      onChange={(e) => handleChange(e.target.value)}
      onFocus={(e) => {
        focused.current = true
        if (selectOnFocus) e.currentTarget.select()
      }}
      onBlur={handleBlur}
    />
  )

  if (!prefix && !suffix) return input
  return (
    <div className={styles.affixed}>
      {prefix && (
        <span className={styles.affix} data-side="start" dir="ltr">
          {prefix}
        </span>
      )}
      {input}
      {suffix && (
        <span className={styles.affix} data-side="end" dir="ltr">
          {suffix}
        </span>
      )}
    </div>
  )
}
