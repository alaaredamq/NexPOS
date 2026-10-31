import { useTranslation } from 'react-i18next'
import styles from './Loader.module.css'

interface LoaderProps {
  size?: 'sm' | 'md' | 'lg'
  /** 'primary' (brand, default) · 'inverse' (white, for use inside filled/colored buttons) · 'muted' */
  tone?: 'primary' | 'inverse' | 'muted'
  label?: string
  /** Centers the loader in a full-height block — for page/section-level loading states. */
  fullSection?: boolean
}

export function Loader({ size = 'md', tone = 'primary', label, fullSection = false }: LoaderProps) {
  const { t } = useTranslation()
  const node = (
    <span
      className={styles.loader}
      data-size={size}
      data-tone={tone}
      role="status"
      aria-label={label ?? t('common.loading', 'Loading')}
    >
      <span className={styles.ring} />
      <span className={`${styles.ring} ${styles.ringDelay}`} />
      <span className={styles.core} />
    </span>
  )

  if (!fullSection) {
    return label ? (
      <span className={styles.inlineWithLabel}>
        {node}
        <span className={styles.label}>{label}</span>
      </span>
    ) : (
      node
    )
  }

  return (
    <div className={styles.section}>
      {node}
      {label && <span className={styles.label}>{label}</span>}
    </div>
  )
}
