import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { NavIcon } from './NavIcon'
import { BELL_ICON } from './navIcons'
import { flattenNav } from './nav'
import styles from './NotificationBell.module.css'

/**
 * Bell + unread badge. The count rides on the session refresh; the dropdown list of recent items and "mark read" belong
 * to track I2 (it owns the notifications API) - until then the bell opens the notifications page.
 */
export function NotificationBell() {
  const { t } = useTranslation()
  const { unreadNotifications, nav } = useSession()
  const page = flattenNav(nav).find((item) => item.key === 'Notifications')
  if (!page) return null

  const label =
    unreadNotifications > 0 ? `${t('Notifications')} (${t('Shell_Unread', { 0: unreadNotifications, defaultValue: '{{0}} unread' })})` : t('Notifications')
  const badge = unreadNotifications > 0 && <span className={styles.badge}>{unreadNotifications > 9 ? '9+' : unreadNotifications}</span>
  const content = (
    <>
      <NavIcon d={BELL_ICON} />
      {badge}
    </>
  )

  return (
    <div className={styles.wrap}>
      {page.migrated ? (
        <Link to={page.routerPath} className={styles.bellBtn} aria-label={label} title={label}>
          {content}
        </Link>
      ) : (
        <a href={page.href} className={styles.bellBtn} aria-label={label} title={label}>
          {content}
        </a>
      )}
    </div>
  )
}
