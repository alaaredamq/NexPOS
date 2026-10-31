import { useState } from 'react'
import { Link, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { needsAttention, useLicense } from '@/core/license/license'
import { useCan } from '@/core/permissions/permissions'
import { useSession } from '@/core/auth/useSession'
import { flattenNav, isActivePath } from './nav'
import styles from './LicenseBanner.module.css'

/**
 * Shown only when the licence needs attention: renewal due (selling still works) or selling stopped. Same rule as the Razor
 * banner. `floating` is for full-screen pages (POS): a small card in the corner that can be dismissed until the next reload.
 */
export function LicenseBanner({ floating = false }: { floating?: boolean }) {
  const { t } = useTranslation()
  const { pathname } = useLocation()
  const license = useLicense()
  const { nav } = useSession()
  const canManage = useCan(Permissions.License_Manage)
  const [dismissed, setDismissed] = useState(false)

  const page = flattenNav(nav).find((item) => item.key === 'License')
  // On the licence screen itself the banner would only repeat what the page says.
  const onLicensePage = page ? isActivePath(pathname, page.routerPath) : false
  if (!needsAttention(license) || onLicensePage || (floating && dismissed)) return null

  const manage =
    canManage && page ? (
      page.migrated ? (
        <Link className={styles.link} to={page.routerPath}>{t('License_Manage')}</Link>
      ) : (
        <a className={styles.link} href={page.href}>{t('License_Manage')}</a>
      )
    ) : null

  return (
    <div className={styles.banner} data-severity={license.severity} data-floating={floating} role="alert">
      <div className={styles.text}>
        <strong>{license.title}</strong> {license.message}
      </div>
      <div className={styles.actions}>
        {manage}
        {floating && (
          <button type="button" className={styles.dismiss} onClick={() => setDismissed(true)} aria-label={t('common.dismiss', 'Dismiss')}>
            ×
          </button>
        )}
      </div>
    </div>
  )
}
