import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { formatDateTime } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { Select, type SelectOption } from '@/ui/Select'
import { toast } from '@/ui/toast'
import { useBackups, useRestoreBackup } from './api'
import { formatBytes } from './bytes'
import styles from './backup.module.css'

const COMPLETED = { page: 1, pageSize: 200, sort: 'createdAt', dir: 'desc' as const, q: '', status: 'Completed' }

/**
 * `/app/backup/restore` - replaces all the data with a backup's. The Razor page restored on one click; this asks first, because nothing
 * entered since the backup survives it. The dialog stays up (spinner on its button) while the request runs, which is the whole wait:
 * the server restores before it answers.
 */
export default function RestorePage() {
  const { t } = useTranslation('backup')
  const backups = useBackups(COMPLETED)
  const restore = useRestoreBackup()
  const [picked, setPicked] = useState<SelectOption<number> | null>(null)
  const [safety, setSafety] = useState(true)

  const options = useMemo<SelectOption<number>[]>(
    () => (backups.data?.items ?? []).map((b) => ({ value: b.id, label: b.name, hint: `${formatDateTime(b.createdAt)} · ${formatBytes(b.fileSizeBytes)}` })),
    [backups.data],
  )

  const start = async () => {
    const backup = backups.data?.items.find((b) => b.id === picked?.value)
    if (!backup) {
      toast.warning(t('PleaseSelectBackupToRestore'))
      return
    }

    await confirm({
      title: t('Restore_ConfirmTitle'),
      message: t('Restore_ConfirmMessage', { 0: backup.name, 1: formatDateTime(backup.createdAt) }),
      detail: safety ? t('Restore_ConfirmWithSafety') : t('Restore_ConfirmNoSafety'),
      confirmLabel: t('Restore_Action'),
      danger: true,
      // A failure (the file is gone, it is not a backup of this database ...) shows inside the dialog, which stays open.
      onConfirm: async () => {
        await restore.mutateAsync({ id: backup.id, createBackupBeforeRestore: safety })
        setPicked(null)
        toast.success(t('Msg_RestoreDone', { 0: formatDateTime(backup.createdAt) }))
      },
    })
  }

  return (
    <div className={styles.card}>
      <h2 className={styles.title}>{t('RestoreFromBackup')}</h2>
      <p className={styles.lead}>{t('Restore_Intro')}</p>
      {backups.isPending ? (
        <Loader size="sm" tone="primary" />
      ) : backups.isError ? (
        <p className={styles.warn} role="alert">
          {backups.error.message}
        </p>
      ) : options.length === 0 ? (
        <p className={styles.muted}>{t('Restore_NoBackups')}</p>
      ) : (
        <>
          <div className={styles.fieldBlock}>
            <label className={styles.nameMain} htmlFor="restore-backup">
              {t('SelectBackup')} *
            </label>
            <Select<number> id="restore-backup" value={picked} onChange={setPicked} options={options} placeholder={t('ChooseBackupToRestore')} aria-required />
          </div>
          <label className={styles.checkRow}>
            <input type="checkbox" className={styles.checkbox} checked={safety} onChange={(event) => setSafety(event.target.checked)} />
            <span>{t('CreateBackupBeforeRestore')}</span>
          </label>
          <div>
            <Button variant="danger" onClick={() => void start()} disabled={!picked}>
              {t('StartRestore')}
            </Button>
          </div>
        </>
      )}
    </div>
  )
}
