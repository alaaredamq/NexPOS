import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDateTime, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { useDeleteSchedule, useRunSchedule, useSchedules, useSetScheduleActive, type ScheduleRow } from './api'
import styles from './backup.module.css'
import { ScheduleFormModal, WEEKDAYS } from './ScheduleFormModal'

/** `/app/backup/schedules` - recurring backups: create, edit, pause / resume, run now, delete. */
export default function SchedulesPage() {
  const { t } = useTranslation(['backup', 'common'])
  const canManage = useCan(Permissions.Backup_Manage)
  const canRun = useCan(Permissions.Backup_Create)
  const list = useSchedules()
  const setActive = useSetScheduleActive()
  const remove = useDeleteSchedule()
  const run = useRunSchedule()
  const [editing, setEditing] = useState<ScheduleRow | 'new' | null>(null)

  const frequencyLabels = {
    Daily: t('Daily', { ns: 'common' }),
    Weekly: t('Weekly', { ns: 'common' }),
    Monthly: t('Monthly', { ns: 'common' }),
  }

  const describe = (s: ScheduleRow) => {
    const time = s.time
    if (s.frequency === 'Weekly') return t('Schedule_Weekly', { 0: frequencyLabels.Weekly, 1: t(WEEKDAYS[s.dayOfWeek ?? 0] ?? 'Sunday'), 2: time })
    if (s.frequency === 'Monthly') return t('Schedule_Monthly', { 0: frequencyLabels.Monthly, 1: formatNumber(s.dayOfMonth ?? 1, 0), 2: time })
    return t('Schedule_Daily', { 0: frequencyLabels.Daily, 1: time })
  }

  const toggle = async (s: ScheduleRow) => {
    const enable = !s.isActive
    try {
      await setActive.mutateAsync({ id: s.id, isActive: enable })
      toast.success(enable ? t('ScheduleEnabledSuccessfully') : t('ScheduleDisabledSuccessfully'))
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('FailedToToggleSchedule'))
    }
  }

  const runNow = async (s: ScheduleRow) => {
    await confirm({
      title: t('RunNow'),
      message: t('Schedule_ConfirmRun'),
      detail: s.name,
      confirmLabel: t('RunNow'),
      // The request lasts as long as the snapshot; a failure shows inside the dialog.
      onConfirm: async () => {
        await run.mutateAsync(s.id)
        toast.success(t('Msg_ScheduleRan'))
      },
    })
  }

  const askDelete = async (s: ScheduleRow) => {
    await confirm({
      title: t('DeleteScheduleTitle'),
      message: t('ConfirmDeleteSchedule'),
      detail: s.name,
      confirmLabel: t('YesDelete'),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(s.id)
        toast.success(t('ScheduleDeletedSuccessfully'))
      },
    })
  }

  const when = (value?: string | null) => (value ? <bdi>{formatDateTime(value)}</bdi> : <span className={styles.muted}>{t('Never')}</span>)

  const columns: Column<ScheduleRow>[] = [
    {
      key: 'name',
      header: t('ScheduleName'),
      required: true,
      cell: (s) => (
        <div className={styles.name}>
          <span className={styles.nameMain}>
            <bdi>{s.name}</bdi>
          </span>
          {s.description && (
            <span className={`${styles.muted} ${styles.clip}`} title={s.description}>
              <bdi>{s.description}</bdi>
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'frequency',
      header: t('Frequency', { ns: 'common' }),
      cell: (s) => (
        <div className={styles.name}>
          <span>{describe(s)}</span>
          <span className={styles.muted}>{s.retentionDays ? t('Schedule_RetentionDays', { 0: formatNumber(s.retentionDays, 0) }) : t('Schedule_KeepsAll')}</span>
        </div>
      ),
    },
    { key: 'nextRun', header: t('NextRun'), hideBelow: 800, cell: (s) => (s.isActive ? when(s.nextRunAt) : <span className={styles.muted}>{t('Schedule_Paused')}</span>) },
    { key: 'lastRun', header: t('LastRun'), hideBelow: 1000, cell: (s) => when(s.lastRunAt) },
    {
      key: 'status',
      header: t('Status'),
      align: 'center',
      cell: (s) => <StatusBadge tone={s.isActive ? 'success' : 'neutral'} label={s.isActive ? t('Active') : t('Inactive')} />,
    },
  ]

  return (
    <div className={styles.panel}>
      <DataTable<ScheduleRow>
        caption={t('BackupSchedules')}
        columns={columns}
        rows={list.data ?? []}
        rowKey={(s) => s.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.isError ? list.error.message : null}
        onRetry={() => void list.refetch()}
        emptyTitle={t('NoSchedulesFound')}
        emptyAction={canManage ? <Button variant="primary" onClick={() => setEditing('new')}>{t('CreateSchedule')}</Button> : undefined}
        rowActionsLabel={t('Actions')}
        rowActions={
          canManage || canRun
            ? (s) => (
                <>
                  {canRun && <IconButton icon="play" label={t('RunNow')} onClick={() => void runNow(s)} />}
                  {canManage && <IconButton icon="edit" label={t('Edit')} onClick={() => setEditing(s)} />}
                  {canManage && (
                    <IconButton icon={s.isActive ? 'minus' : 'check'} label={s.isActive ? t('Disable') : t('Enable')} onClick={() => void toggle(s)} />
                  )}
                  {canManage && <IconButton icon="trash" tone="danger" label={t('Delete')} onClick={() => void askDelete(s)} />}
                </>
              )
            : undefined
        }
        toolbar={
          canManage && (list.data?.length ?? 0) > 0 ? (
            <Button variant="primary" onClick={() => setEditing('new')}>
              {t('CreateSchedule')}
            </Button>
          ) : undefined
        }
      />
      {editing && <ScheduleFormModal schedule={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
