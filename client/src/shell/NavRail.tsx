import { Link, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { NavIcon } from './NavIcon'
import { MENU_ICON, navIconPath } from './navIcons'
import { flattenNav, isActivePath } from './nav'
import { useShellStore } from './shellStore'
import { DRAWER_QUERY, useMediaQuery } from './useMediaQuery'
import styles from './NavRail.module.css'

/**
 * The rail, built from `session.nav`: already filtered to what the user may open, grouped, labelled in their language.
 * Migrated pages are router links; the rest are plain anchors to the Razor page (full navigation, same cookie).
 */
export function NavRail() {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const { nav, branding } = useSession()
  const collapsed = useShellStore((s) => s.railCollapsed)
  const drawerOpen = useShellStore((s) => s.drawerOpen)
  const toggleRail = useShellStore((s) => s.toggleRail)
  const setDrawerOpen = useShellStore((s) => s.setDrawerOpen)
  const drawerMode = useMediaQuery(DRAWER_QUERY)

  const expanded = drawerMode || !collapsed
  const sections = nav.map((section) => ({ ...section, items: flattenNav([section]) }))

  return (
    <nav
      className={styles.rail}
      data-open={expanded}
      data-drawer={drawerMode ? drawerOpen : undefined}
      aria-label={t('Navigation')}
    >
      <div className={styles.brandRow}>
        <div className={styles.brandIcon}>
          <img className={styles.brandImg} src={branding.markUrl} alt="" />
        </div>
        <div className={styles.brandText}>
          <div className={styles.brandName}>{branding.appName}</div>
          <div className={styles.brandTag}>{t('PointOfSaleSystem')}</div>
        </div>
      </div>

      <div className={styles.scroll}>
        {sections.map((section) => (
          <div key={section.key} className={styles.group}>
            {/* Collapsed, the label would be a truncated smear - the rule stands in for it. */}
            <div className={styles.groupLabel}>{section.label}</div>
            <div className={styles.groupRule} />
            {section.items.map((item) => {
              const active = isActivePath(pathname, item.routerPath)
              const className = `${styles.row} ${active ? styles.active : ''}`
              const content = (
                <>
                  <span className={styles.icon}>
                    <NavIcon d={navIconPath(item.icon)} />
                  </span>
                  <span className={styles.label}>{item.label}</span>
                </>
              )
              return item.migrated ? (
                <Link key={item.key} to={item.routerPath} title={item.label} className={className} aria-current={active ? 'page' : undefined}>
                  {content}
                </Link>
              ) : (
                <a key={item.key} href={item.href} title={item.label} className={className} aria-current={active ? 'page' : undefined}>
                  {content}
                </a>
              )
            })}
          </div>
        ))}
      </div>

      {drawerMode ? (
        <button type="button" className={`${styles.row} ${styles.toggle}`} onClick={() => setDrawerOpen(false)}>
          <span className={styles.icon}>
            <NavIcon d="M6 6l12 12 M18 6L6 18" />
          </span>
          <span className={styles.label}>{t('Shell_CloseMenu', 'Close menu')}</span>
        </button>
      ) : (
        <button type="button" className={`${styles.row} ${styles.toggle}`} onClick={toggleRail} aria-expanded={expanded}>
          <span className={styles.icon}>
            <NavIcon d={MENU_ICON} />
          </span>
          <span className={styles.label}>{expanded ? t('CollapseMenu') : t('ExpandMenu')}</span>
        </button>
      )}
    </nav>
  )
}
