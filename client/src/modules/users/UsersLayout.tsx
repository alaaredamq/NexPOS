import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router-dom'
import { formatNumber } from '@/ui/format'
import { StatTile } from '@/ui/StatTile'
import { useUserSummary } from './api'
import styles from './users.module.css'

/** The four tiles and the Users / Roles tabs shared by both pages of the module. */
export function UsersLayout({ children }: { children: ReactNode }) {
  const { t } = useTranslation('users')
  const summary = useUserSummary().data
  const tile = (n: number | undefined) => (n === undefined ? '-' : formatNumber(n, 0))

  return (
    <>
      <div className={styles.tiles}>
        <StatTile label={t('TotalUsers')} value={tile(summary?.totalUsers)} />
        <StatTile label={t('TotalRoles')} value={tile(summary?.totalRoles)} />
        <StatTile label={t('ActiveUsers')} value={tile(summary?.activeUsers)} tone="success" />
        <StatTile label={t('AdminUsers')} value={tile(summary?.adminUsers)} sub={t('SystemAdministrators')} />
      </div>
      <nav className={styles.tabs} aria-label={t('UserManagement')}>
        <NavLink to="/users" end className={styles.tab}>
          {t('Users')}
        </NavLink>
        <NavLink to="/users/roles" className={styles.tab}>
          {t('Roles')}
        </NavLink>
      </nav>
      {children}
    </>
  )
}
