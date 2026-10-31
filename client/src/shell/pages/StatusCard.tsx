import type { ReactNode } from 'react'
import styles from './StatusPage.module.css'

type Glyph = 'lock' | 'search' | 'key' | 'alert' | 'plug' | 'clock'

const GLYPHS: Record<Glyph, string> = {
  lock: 'M5 11h14v9H5z M8 11V8a4 4 0 018 0v3',
  search: 'M10.5 17a6.5 6.5 0 100-13 6.5 6.5 0 000 13z M15.5 15.5L20 20',
  key: 'M15 8a4 4 0 11-3.9 4.9L3 21v-3h3v-3h3l2.1-2.1A4 4 0 0115 8z',
  alert: 'M12 4l9 16H3z M12 10v4.5 M12 17.3v.2',
  plug: 'M9 3v5 M15 3v5 M6 8h12v3a6 6 0 01-12 0z M12 17v4',
  clock: 'M12 21a9 9 0 100-18 9 9 0 000 18z M12 7v5l3 2',
}

interface StatusCardProps {
  glyph: Glyph
  title: string
  children?: ReactNode
  actions?: ReactNode
}

/** The centred card used by every full-area state of the shell: forbidden, not found, licence stopped, server down, placeholder. */
export function StatusCard({ glyph, title, children, actions }: StatusCardProps) {
  return (
    <div className={styles.wrap}>
      <div className={styles.card} role="status">
        <div className={styles.icon}>
          <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d={GLYPHS[glyph]} />
          </svg>
        </div>
        <h2 className={styles.title}>{title}</h2>
        {children && <div className={styles.body}>{children}</div>}
        {actions && <div className={styles.actions}>{actions}</div>}
      </div>
    </div>
  )
}
