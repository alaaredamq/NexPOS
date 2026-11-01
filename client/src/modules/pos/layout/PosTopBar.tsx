import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { DrawerChip } from '@/shell/DrawerChip'
import { LanguageToggle } from '@/shell/LanguageToggle'
import { UserMenu } from '@/shell/UserMenu'
import { flattenNav } from '@/shell/nav'
import { Icon } from '@/ui/Icon'
import { usePosUi } from '../slots'
import styles from './layout.module.css'

function Clock() {
  const [now, setNow] = useState(() => new Date())
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), 1000)
    return () => window.clearInterval(id)
  }, [])
  // en-US on purpose: the old till printed Latin digits and 12-hour time in both languages.
  return <time className={styles.clock}>{now.toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}</time>
}

interface PosTopBarProps {
  onShowShortcuts: () => void
  /** Narrow screens stack the columns; this scrolls the cart into view. */
  onReviewCart: () => void
}

/** The till's own header: logo, a way back to management, drawer status, clock, language and user. No rail. */
export function PosTopBar({ onShowShortcuts, onReviewCart }: PosTopBarProps) {
  const { t } = useTranslation(['pos', 'common'])
  const { nav, branding } = useSession()
  const cartCount = usePosUi((s) => s.cartCount)

  // The management home: the dashboard when this role has it, otherwise the first page that is not the till.
  const items = flattenNav(nav).filter((item) => item.key !== 'POS')
  const management = items.find((item) => item.key === 'Dashboard') ?? items[0]

  return (
    <header className={styles.topBar}>
      <img className={styles.logo} src={branding.logoUrl} alt={branding.appName} />
      <h1 className={styles.srOnly}>{t('PointOfSale')}</h1>

      {management &&
        (management.migrated ? (
          <Link className={styles.mgmtLink} to={management.routerPath} title={t('GoToManagement')}>
            <Icon name="chevron-start" size={16} />
            {t('Management')}
          </Link>
        ) : (
          <a className={styles.mgmtLink} href={management.href} title={t('GoToManagement')}>
            <Icon name="chevron-start" size={16} />
            {t('Management')}
          </a>
        ))}

      <div className={styles.spacer} />

      <button type="button" className={styles.reviewCart} onClick={onReviewCart} aria-label={t('reviewCart')}>
        <Icon name="cart" size={18} />
        <span className={styles.badge}>{cartCount}</span>
      </button>
      <DrawerChip />
      <Clock />
      <button type="button" className={styles.iconBtn} onClick={onShowShortcuts} aria-label={t('KeyboardShortcuts')} title={t('KeyboardShortcuts')}>
        <kbd>?</kbd>
      </button>
      <LanguageToggle />
      <UserMenu />
    </header>
  )
}
