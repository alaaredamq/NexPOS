import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import styles from './PinPad.module.css'

const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', 'C', '0', '⌫']

interface PinPadProps {
  pin: string
  onKey: (key: string) => void
  dotCount?: number
  shake?: boolean
  /** Allow entering the PIN from a physical keyboard. Defaults to on. */
  keyboard?: boolean
}

/**
 * Digits are always laid out 1-2-3 left-to-right (a keypad is not text), so the grid is forced LTR.
 * Keys report `'0'-'9'`, `'C'` (clear), `'⌫'` (backspace) and, from the keyboard only, `'Enter'`.
 */
export function PinPad({ pin, onKey, dotCount = 4, shake = false, keyboard = true }: PinPadProps) {
  const { t } = useTranslation()

  useEffect(() => {
    if (!keyboard) return
    const handler = (e: KeyboardEvent) => {
      if (e.metaKey || e.ctrlKey || e.altKey) return
      // Don't hijack typing in a real input field elsewhere on the page.
      const target = e.target as HTMLElement | null
      if (target && (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA' || target.isContentEditable)) return

      if (e.key >= '0' && e.key <= '9') {
        onKey(e.key)
        e.preventDefault()
      } else if (e.key === 'Backspace') {
        onKey('⌫')
        e.preventDefault()
      } else if (e.key === 'Delete' || e.key === 'Escape') {
        // Escape is also the Modal's close key; a PIN pad in a modal wants it to clear first only when
        // there is something to clear, otherwise let it through.
        if (e.key === 'Escape' && pin.length === 0) return
        onKey('C')
        e.preventDefault()
      } else if (e.key === 'Enter') {
        // Enter belongs to the pad, not to whatever button was last clicked. Without this, the pad key
        // that still holds focus gets re-activated.
        e.preventDefault()
        onKey('Enter')
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [keyboard, onKey, pin.length])

  return (
    <div className={styles.wrap}>
      <div
        className={styles.dots}
        data-shake={shake}
        role="img"
        aria-label={t('common.pinEntered', { count: pin.length, total: dotCount, defaultValue: '{{count}} of {{total}} digits entered' })}
      >
        {Array.from({ length: dotCount }).map((_, i) => (
          <div key={i} className={styles.dot} data-filled={i < pin.length} />
        ))}
      </div>
      <div className={styles.keys} dir="ltr">
        {KEYS.map((k) => (
          <button
            key={k}
            type="button"
            className={styles.key}
            data-muted={k === 'C' || k === '⌫'}
            aria-label={k === 'C' ? t('common.clear', 'Clear') : k === '⌫' ? t('common.backspace', 'Backspace') : undefined}
            onClick={() => onKey(k)}
          >
            {k}
          </button>
        ))}
      </div>
    </div>
  )
}
