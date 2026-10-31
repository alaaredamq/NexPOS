import { Suspense, useEffect } from 'react'
import { Outlet, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Loader } from '@/ui/Loader'
import { CommandPalette } from './CommandPalette'
import { ErrorBoundary } from './ErrorBoundary'
import { LicenseBanner } from './LicenseBanner'
import { NavRail } from './NavRail'
import { TopBar } from './TopBar'
import { useShellStore } from './shellStore'
import { DRAWER_QUERY, useMediaQuery } from './useMediaQuery'
import styles from './AppShell.module.css'

/** The chrome around every normal page: rail, top bar, licence banner, then the routed page. Full-screen pages (POS) opt out via `handle: { layout: 'bare' }`. */
export function AppShell() {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const drawerOpen = useShellStore((s) => s.drawerOpen)
  const setDrawerOpen = useShellStore((s) => s.setDrawerOpen)
  const drawerMode = useMediaQuery(DRAWER_QUERY)

  // Picking a page closes the drawer.
  useEffect(() => {
    setDrawerOpen(false)
  }, [pathname, setDrawerOpen])

  return (
    <div className={styles.shell}>
      <NavRail />
      {drawerMode && drawerOpen && <button type="button" className={styles.backdrop} aria-label={t('Shell_CloseMenu', 'Close menu')} onClick={() => setDrawerOpen(false)} />}
      <div className={styles.main}>
        <TopBar />
        <LicenseBanner />
        <main className={styles.content}>
          <ErrorBoundary>
            <Suspense fallback={<Loader fullSection size="lg" />}>
              <Outlet />
            </Suspense>
          </ErrorBoundary>
        </main>
      </div>
      <CommandPalette />
    </div>
  )
}
