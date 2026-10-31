import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { Permissions } from '@/api/permissions'
import { useSession } from '@/core/auth/useSession'
import { useSignOut } from '@/core/auth/useSignOut'
import { useCan } from '@/core/permissions/permissions'
import { flattenNav, landingPath } from '../nav'
import { StatusCard } from './StatusCard'
import styles from './StatusPage.module.css'

/** 403: the route guard bounced the user here. */
export function ForbiddenPage() {
  const { t } = useTranslation()
  const session = useSession()
  const home = landingPath(session)
  return (
    <StatusCard
      glyph="lock"
      title={t('AccessDenied')}
      actions={home && <Link className={styles.link} to={home}>{t('Shell_GoHome')}</Link>}
    >
      {t('Shell_ForbiddenBody')}
    </StatusCard>
  )
}

/** Unknown address inside the app. */
export function NotFoundPage() {
  const { t } = useTranslation()
  const session = useSession()
  const home = landingPath(session)
  return (
    <StatusCard
      glyph="search"
      title={t('Shell_NotFoundTitle')}
      actions={home && <Link className={styles.link} to={home}>{t('Shell_GoHome')}</Link>}
    >
      {t('Shell_NotFoundBody')}
    </StatusCard>
  )
}

/** A role that grants no page at all: a real state right after a role is emptied. Shown instead of redirecting in a loop. */
export function NoPagesPage() {
  const { t } = useTranslation()
  const signOut = useSignOut()
  return (
    <StatusCard glyph="lock" title={t('Shell_NoPages')} actions={<Button onClick={() => void signOut({ forget: true })}>{t('Logout')}</Button>}>
      {t('Shell_NoPagesBody')}
    </StatusCard>
  )
}

/** Where the sales routes (`RequireSalesLicense`) send people while the licence stops selling. */
export function LicenseStoppedPage() {
  const { t } = useTranslation()
  const { license, nav } = useSession()
  const canManage = useCan(Permissions.License_Manage)
  const page = flattenNav(nav).find((item) => item.key === 'License')
  const manage =
    canManage && page ? (
      page.migrated ? (
        <Link className={styles.link} to={page.routerPath}>{t('License_Manage')}</Link>
      ) : (
        <a className={styles.link} href={page.href}>{t('License_Manage')}</a>
      )
    ) : undefined
  return (
    <StatusCard glyph="key" title={t('Shell_SalesStopped')} actions={manage}>
      <strong>{license.title}</strong>
      <br />
      {license.message}
    </StatusCard>
  )
}

/**
 * Lowest-priority route (`*`): shows a real module's page when one is registered, so this only renders for pages that have a
 * rail entry but no React version yet - and for addresses that match nothing.
 */
export function ModulePlaceholder() {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const navigate = useNavigate()
  const { nav } = useSession()
  const item = flattenNav(nav).find((entry) => pathname === entry.routerPath || pathname.startsWith(`${entry.routerPath}/`))

  if (!item) return <NotFoundPage />

  return (
    <StatusCard
      glyph="clock"
      title={item.label}
      actions={
        <>
          <a className={styles.link} href={item.legacyPath}>{t('Shell_OpenCurrent')}</a>
          <Button variant="ghost" onClick={() => void navigate(-1)}>{t('Back')}</Button>
        </>
      }
    >
      {t('Shell_PlaceholderBody')}
    </StatusCard>
  )
}
