import { useId } from 'react'
import { useTranslation } from 'react-i18next'
import { toDateInputValue } from './format'
import styles from './DateRange.module.css'

export interface DateRangeValue {
  /** `yyyy-mm-dd` or `''` for open-ended. */
  from: string
  to: string
}

interface DateRangeProps {
  value: DateRangeValue
  onChange: (value: DateRangeValue) => void
  /** Show Today / 7 days / 30 days / This month quick picks. */
  presets?: boolean
  disabled?: boolean
}

function presetRange(kind: 'today' | '7d' | '30d' | 'month'): DateRangeValue {
  const today = new Date()
  const to = toDateInputValue(today)
  const start = new Date(today)
  if (kind === '7d') start.setDate(start.getDate() - 6)
  else if (kind === '30d') start.setDate(start.getDate() - 29)
  else if (kind === 'month') start.setDate(1)
  return { from: toDateInputValue(start), to }
}

/** From/to pair for report and list filters. Keeps `from <= to` by bounding each input with the other. */
export function DateRange({ value, onChange, presets = true, disabled }: DateRangeProps) {
  const { t } = useTranslation()
  const id = useId()

  const set = (patch: Partial<DateRangeValue>) => {
    const next = { ...value, ...patch }
    // Typing a "from" after the current "to" drags "to" along instead of leaving an inverted range.
    if (next.from && next.to && next.from > next.to) {
      if (patch.from !== undefined) next.to = next.from
      else next.from = next.to
    }
    onChange(next)
  }

  const picks = [
    { kind: 'today', label: t('common.today', 'Today') },
    { kind: '7d', label: t('common.last7Days', '7 days') },
    { kind: '30d', label: t('common.last30Days', '30 days') },
    { kind: 'month', label: t('common.thisMonth', 'This month') },
  ] as const

  return (
    <div className={styles.wrap} role="group" aria-label={t('common.dateRange', 'Date range')}>
      <label className={styles.item} htmlFor={`${id}-from`}>
        <span className={styles.caption}>{t('common.from', 'From')}</span>
        <input id={`${id}-from`} type="date" className={styles.input} value={value.from} max={value.to || undefined} disabled={disabled} onChange={(e) => set({ from: e.target.value })} />
      </label>
      <label className={styles.item} htmlFor={`${id}-to`}>
        <span className={styles.caption}>{t('common.to', 'To')}</span>
        <input id={`${id}-to`} type="date" className={styles.input} value={value.to} min={value.from || undefined} disabled={disabled} onChange={(e) => set({ to: e.target.value })} />
      </label>
      {presets && (
        <div className={styles.presets}>
          {picks.map((p) => (
            <button key={p.kind} type="button" className={styles.preset} disabled={disabled} onClick={() => onChange(presetRange(p.kind))}>
              {p.label}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}
