import { Link } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { formatMoney } from '@/ui/format'
import { flattenNav } from './nav'
import styles from './TopBar.module.css'

/**
 * Whether the user's cash drawer is open, with the opening cash. `session.drawer` is null for people who cannot use a drawer,
 * so the chip simply is not there for them (telling them it is closed would be noise). Kept fresh by the 60 s session refresh
 * and the refetch on window focus; S2 swaps in a dedicated endpoint with the same shape.
 */
export function DrawerChip() {
  const { t } = useTranslation()
  const { drawer, nav } = useSession()
  if (!drawer) return null

  const page = flattenNav(nav).find((item) => item.key === 'CashDrawer')
  const label = drawer.isOpen ? t('DrawerOpenWithAmount', { 0: formatMoney(drawer.openingCash, '$') }) : t('DrawerNotOpened')
  const props = { className: styles.tillChip, 'data-open': drawer.isOpen, title: label }
  const content = (
    <>
      <span className={styles.tillDot} aria-hidden="true" />
      <span>{label}</span>
    </>
  )

  if (!page) return <span {...props}>{content}</span>
  return page.migrated ? (
    <Link to={page.routerPath} {...props}>
      {content}
    </Link>
  ) : (
    <a href={page.href} {...props}>
      {content}
    </a>
  )
}
