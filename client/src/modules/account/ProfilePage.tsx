import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Loader } from '@/ui/Loader'
import { formatDateTime } from '@/ui/format'
import { PageHeader } from '@/ui/PageHeader'
import { StatusBadge } from '@/ui/StatusBadge'
import { useProfile } from './api'
import styles from './account.module.css'

/** `/app/account/profile` - who you are, your role and when you last signed in. */
export default function ProfilePage() {
  const { t } = useTranslation(['account', 'common'])
  const profile = useProfile()
  const p = profile.data

  const rows: { label: string; value: React.ReactNode }[] = p
    ? [
        { label: t('Username', { ns: 'common' }), value: <bdi>{p.username}</bdi> },
        { label: t('EmailAddress', { ns: 'common' }), value: <bdi>{p.email}</bdi> },
        { label: t('Prof_FullName', { ns: 'common' }), value: <bdi>{p.fullName}</bdi> },
        { label: t('Prof_Role', { ns: 'common' }), value: <StatusBadge tone="info" label={p.roleName} /> },
        { label: t('LastSignIn', { ns: 'account' }), value: p.lastLoginAt ? <bdi>{formatDateTime(p.lastLoginAt)}</bdi> : t('Never', { ns: 'common' }) },
      ]
    : []

  return (
    <div className={styles.page}>
      <PageHeader title={t('Profile', { ns: 'common' })} />
      <section className={styles.card} aria-labelledby="profile-info">
        <h2 id="profile-info" className={styles.cardTitle}>
          {t('Prof_AccountInformation', { ns: 'common' })}
        </h2>
        {profile.isPending && <Loader fullSection />}
        {profile.isError && <p className={styles.error}>{profile.error.message}</p>}
        {p && (
          <dl className={styles.facts}>
            {rows.map((row) => (
              <div key={row.label} className={styles.fact}>
                <dt>{row.label}</dt>
                <dd>{row.value}</dd>
              </div>
            ))}
          </dl>
        )}
        <div className={styles.cardActions}>
          <Link className={styles.linkButton} to="/account/change-pin">
            {t('ChangePin', { ns: 'common' })}
          </Link>
        </div>
      </section>
    </div>
  )
}
