import { forwardRef, type KeyboardEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from './Icon'
import styles from './ScanInput.module.css'

interface ScanInputProps {
  value: string
  onChange: (value: string) => void
  /** Fires on Enter — barcode scanners terminate a scan with Enter. */
  onEnter?: () => void
  placeholder: string
  size?: 'lg' | 'sm'
  hint?: string
  /** Shown in place of the ready pill, with an error treatment — e.g. "Not found". */
  error?: string
  'aria-label'?: string
}

export const ScanInput = forwardRef<HTMLInputElement, ScanInputProps>(function ScanInput(
  { value, onChange, onEnter, placeholder, size = 'sm', hint, error, 'aria-label': ariaLabel },
  ref,
) {
  const { t } = useTranslation()
  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter') onEnter?.()
  }

  return (
    <div className={styles.wrap} data-size={size}>
      <div className={styles.field}>
        <span className={styles.searchIcon}>
          <Icon name="search" size={size === 'lg' ? 20 : 16} />
        </span>
        <input
          ref={ref}
          className={styles.input}
          data-error={error ? true : undefined}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder={placeholder}
          aria-label={ariaLabel ?? placeholder}
          aria-invalid={error ? true : undefined}
          autoComplete="off"
          spellCheck={false}
        />
        <span className={styles.pill} data-error={error ? true : undefined} role={error ? 'alert' : undefined}>
          <span className={styles.dot} />
          {error || (size === 'lg' ? t('common.scannerReady', 'SCANNER READY') : t('common.scanReady', 'SCAN READY'))}
        </span>
      </div>
      {hint && <div className={styles.hint}>{hint}</div>}
    </div>
  )
})
