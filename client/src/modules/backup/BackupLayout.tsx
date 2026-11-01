import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink, Outlet } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { formatDate, formatDateTime, formatNumber } from '@/ui/format'
import { PageHeader } from '@/ui/PageHeader'
import { StatTile } from '@/ui/StatTile'
import { toast } from '@/ui/toast'
import { useBackupSummary, useTestConnection } from './api'
import { formatBytes } from './bytes'
import { CreateBackupModal } from './CreateBackupModal'
import styles from './backup.module.css'

/** The header, the four tiles and the section tabs shared by the pages of the module. */
export default function BackupLayout() {
  const { t } = useTranslation('backup')
  const canCreate = useCan(Permissions.Backup_Create)
  const canRestore = useCan(Permissions.Backup_Restore)
  const summary = useBackupSummary().data
  const test = useTestConnection()
  const [creating, setCreating] = useState(false)

  const runTest = async () => {
    try {
      const result = await test.mutateAsync()
      if (result.backupConnection && result.restoreConnection) toast.success(t('Msg_ConnectionsOk'))
      else toast.error(t('ConnectionTestFailed'))
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('ConnectionTestFailed'))
    }
  }

  const lastFailed = summary?.lastBackupStatus === 'Failed'

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('BackupAndRestoreManagement')}
        primaryActionLabel={canCreate ? t('CreateBackup') : undefined}
        onPrimaryAction={canCreate ? () => setCreating(true) : undefined}
      >
        <Button onClick={() => void runTest()} loading={test.isPending}>
          {t('TestConnections')}
        </Button>
      </PageHeader>

      <div className={styles.tiles}>
        <StatTile
          label={t('TotalBackups')}
          value={summary ? formatNumber(summary.totalBackups, 0) : '-'}
          sub={summary ? `${formatNumber(summary.successfulBackups, 0)} ${t('Successful')}` : undefined}
        />
        <StatTile label={t('TotalSize')} value={summary ? formatBytes(summary.totalSizeBytes) : '-'} sub={t('StorageUsed')} />
        <StatTile
          label={t('Scheduled')}
          value={summary ? formatNumber(summary.scheduledCount, 0) : '-'}
          sub={`${t('Next')}: ${summary?.nextScheduledAt ? formatDateTime(summary.nextScheduledAt) : t('None')}`}
        />
        <StatTile
          label={t('LastBackup')}
          value={summary ? (summary.lastBackupAt ? formatDate(summary.lastBackupAt) : t('Never')) : '-'}
          sub={lastFailed ? t('LastBackupFailed') : undefined}
          tone={lastFailed ? 'danger' : 'default'}
        />
      </div>

      <nav className={styles.tabs} aria-label={t('Backup_Aria_Tabs')}>
        <NavLink to="/backup" end className={styles.tab}>
          {t('Backups')}
        </NavLink>
        {canRestore && (
          <NavLink to="/backup/restore" className={styles.tab}>
            {t('Restore')}
          </NavLink>
        )}
        <NavLink to="/backup/schedules" className={styles.tab}>
          {t('Schedule')}
        </NavLink>
        <NavLink to="/backup/system" className={styles.tab}>
          {t('SystemInformation')}
        </NavLink>
      </nav>

      <Outlet />

      {creating && <CreateBackupModal onClose={() => setCreating(false)} />}
    </div>
  )
}
