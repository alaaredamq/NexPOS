import { useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { DrawerChip } from './DrawerChip'
import { LanguageToggle } from './LanguageToggle'
import { NavIcon } from './NavIcon'
import { NotificationBell } from './NotificationBell'
import { UserMenu } from './UserMenu'
import { MENU_ICON, SEARCH_ICON } from './navIcons'
import { findCurrent, flattenNav } from './nav'
import { useShellStore } from './shellStore'
import { DRAWER_QUERY, useMediaQuery } from './useMediaQuery'
import styles from './TopBar.module.css'

export function TopBar() {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const { nav, branding } = useSession()
  const setPaletteOpen = useShellStore((s) => s.setPaletteOpen)
  const setDrawerOpen = useShellStore((s) => s.setDrawerOpen)
  const drawerMode = useMediaQuery(DRAWER_QUERY)

  // The page title and its rail section ("Sales / Orders"). Pages with no rail entry (404, forbidden) show the app name.
  const current = findCurrent(flattenNav(nav), pathname)

  return (
    <header className={styles.header}>
      {drawerMode && (
        <button type="button" className={styles.menuBtn} onClick={() => setDrawerOpen(true)} aria-label={t('Menu')} title={t('Menu')}>
          <NavIcon d={MENU_ICON} />
        </button>
      )}

      <div className={styles.titleBlock}>
        {current && <div className={styles.eyebrow}>{current.sectionLabel}</div>}
        <h1 className={styles.title}>{current?.label ?? branding.appName}</h1>
      </div>

      <div className={styles.spacer} />

      <button type="button" className={styles.searchBtn} onClick={() => setPaletteOpen(true)} aria-haspopup="dialog" aria-label={t('JumpTo')}>
        <NavIcon d={SEARCH_ICON} size={15} />
        <span className={styles.searchLabel}>{t('JumpTo')}</span>
        <kbd className={styles.kbd}>Ctrl K</kbd>
      </button>

      <DrawerChip />
      <LanguageToggle />
      <NotificationBell />
      <UserMenu />
    </header>
  )
}
