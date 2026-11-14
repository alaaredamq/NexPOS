import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router-dom'
import { PageHeader } from '@/ui/PageHeader'
import styles from './settings.module.css'

/** Title and the three tabs shared by the pages of the module (the Razor landing page of cards, as a tab bar). */
export function SettingsLayout({ children }: { children: ReactNode }) {
  const { t } = useTranslation(['settings', 'common'])

  return (
    <div className={styles.page}>
      <PageHeader title={t('Settings', { ns: 'common' })} />
      <nav className={styles.tabs} aria-label={t('SectionsLabel', { ns: 'settings' })}>
        <NavLink to="/settings/store" className={styles.tab}>
          {t('Set_StoreReceipts', { ns: 'common' })}
        </NavLink>
        <NavLink to="/settings/printers" className={styles.tab}>
          {t('Set_PrintersLabels', { ns: 'common' })}
        </NavLink>
        <NavLink to="/settings/branding" className={styles.tab}>
          {t('Set_Branding', { ns: 'common' })}
        </NavLink>
        <NavLink to="/settings/online" className={styles.tab}>
          {t('Set_Online', { ns: 'common' })}
        </NavLink>
      </nav>
      {children}
    </div>
  )
}
