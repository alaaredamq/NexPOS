import { useTranslation } from 'react-i18next'
import { Loader } from '@/ui/Loader'
import { useBackupSummary } from './api'
import { formatBytes } from './bytes'
import styles from './backup.module.css'

/** `/app/backup/system` - where the backups live and how much room is left (the Razor "System information" card). */
export default function SystemPage() {
  const { t } = useTranslation('backup')
  const summary = useBackupSummary()

  return (
    <div className={styles.card}>
      <h2 className={styles.title}>{t('SystemInformation')}</h2>
      {summary.isPending ? (
        <Loader size="sm" tone="primary" />
      ) : summary.isError ? (
        <p className={styles.warn} role="alert">
          {summary.error.message}
        </p>
      ) : (
        <>
          <dl className={styles.facts}>
            <dt>{t('BackupFolder')}</dt>
            <dd dir="ltr">{summary.data.backupDirectory}</dd>
            <dt>{t('FreeSpace')}</dt>
            <dd>{formatBytes(summary.data.availableDiskBytes)}</dd>
            <dt>{t('DatabaseSize')}</dt>
            <dd>{formatBytes(summary.data.databaseSizeBytes)}</dd>
            <dt>{t('TotalSize')}</dt>
            <dd>{formatBytes(summary.data.totalSizeBytes)}</dd>
          </dl>
          <p className={styles.hint}>{t('BackupFolderHint')}</p>
        </>
      )}
    </div>
  )
}
