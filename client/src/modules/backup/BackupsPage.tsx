import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDateTime, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import {
  downloadBackup,
  STATUS_FILTERS,
  useBackups,
  useBulkDelete,
  useBulkValidate,
  useDeleteBackup,
  useValidateBackup,
  type BackupRow,
} from './api'
import { formatBytes } from './bytes'
import styles from './backup.module.css'

const DEFAULT_SORT = { key: 'createdAt', dir: 'desc' } as const

export const statusTone = (status: string) =>
  status === 'Completed' ? 'success' : status === 'Failed' ? 'danger' : status === 'InProgress' ? 'warning' : 'neutral'

/** `/app/backup` - the backups: download, check, delete (one or several). */
export default function BackupsPage() {
  const { t } = useTranslation('backup')
  const canManage = useCan(Permissions.Backup_Manage)
  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const [status, setStatus] = useState<string>('')
  const list = useBackups({ ...params, status })
  const validate = useValidateBackup()
  const remove = useDeleteBackup()
  const bulkRemove = useBulkDelete()
  const bulkValidate = useBulkValidate()
  const [selected, setSelected] = useState<Set<number>>(new Set())

  const rows = list.data?.items ?? []
  const allOnPage = rows.length > 0 && rows.every((row) => selected.has(row.id))
  const toggle = (id: number) =>
    setSelected((current) => {
      const next = new Set(current)
      if (!next.delete(id)) next.add(id)
      return next
    })
  const togglePage = () =>
    setSelected((current) => {
      const next = new Set(current)
      for (const row of rows) {
        if (allOnPage) next.delete(row.id)
        else next.add(row.id)
      }
      return next
    })

  const check = async (backup: BackupRow) => {
    try {
      const result = await validate.mutateAsync(backup.id)
      if (result.isValid) toast.success(t('BackupIsValid'))
      else toast.warning(result.errors[0] ?? t('BackupValidationFailed'))
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('FailedToValidateBackup'))
    }
  }

  const askDelete = async (backup: BackupRow) => {
    await confirm({
      title: t('DeleteBackupTitle'),
      message: t('ConfirmDeleteBackup'),
      detail: backup.name,
      confirmLabel: t('YesDelete'),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(backup.id)
        setSelected((current) => {
          const next = new Set(current)
          next.delete(backup.id)
          return next
        })
        toast.success(t('BackupDeletedSuccessfully'))
      },
    })
  }

  const askBulkDelete = async () => {
    const ids = [...selected]
    await confirm({
      title: t('BulkDeleteTitle'),
      message: t('ConfirmDeleteBackups', { 0: formatNumber(ids.length, 0) }),
      confirmLabel: t('YesDelete'),
      danger: true,
      onConfirm: async () => {
        const result = await bulkRemove.mutateAsync(ids)
        setSelected(new Set())
        toast.success(t('Msg_BackupsDeleted', { 0: formatNumber(result.deleted, 0) }))
      },
    })
  }

  const checkSelected = async () => {
    try {
      const result = await bulkValidate.mutateAsync([...selected])
      if (result.allValid) toast.success(t('Msg_AllValid'))
      else toast.warning(t('Msg_SomeInvalid', { 0: formatNumber(result.results.filter((r) => !r.isValid).length, 0) }))
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('FailedToValidateBackups'))
    }
  }

  const columns: Column<BackupRow>[] = [
    {
      key: 'select',
      header: (
        <input
          type="checkbox"
          className={styles.checkbox}
          checked={allOnPage}
          onChange={togglePage}
          aria-label={t('SelectAll')}
          disabled={rows.length === 0}
        />
      ),
      width: 48,
      required: true,
      cell: (b) => (
        <input
          type="checkbox"
          className={styles.checkbox}
          checked={selected.has(b.id)}
          onChange={() => toggle(b.id)}
          aria-label={t('SelectBackupRow', { 0: b.name })}
        />
      ),
    },
    {
      key: 'name',
      header: t('Name'),
      sortable: true,
      required: true,
      cell: (b) => (
        <div className={styles.name}>
          <span className={styles.nameMain}>
            <bdi>{b.name}</bdi>
          </span>
          {b.description && (
            <span className={`${styles.muted} ${styles.clip}`} title={b.description}>
              <bdi>{b.description}</bdi>
            </span>
          )}
          {b.errorMessage && <span className={styles.warn}>{b.errorMessage}</span>}
        </div>
      ),
    },
    {
      key: 'status',
      header: t('Status'),
      align: 'center',
      cell: (b) => <StatusBadge tone={statusTone(b.status)} label={t(`Status_${b.status}`, b.status)} />,
    },
    { key: 'size', header: t('Size'), sortable: true, align: 'end', cell: (b) => formatBytes(b.fileSizeBytes) },
    { key: 'createdAt', header: t('Created'), sortable: true, hideBelow: 700, cell: (b) => <bdi>{formatDateTime(b.createdAt)}</bdi> },
  ]

  return (
    <div className={styles.panel}>
      <DataTable<BackupRow>
        caption={t('BackupHistory')}
        columns={columns}
        rows={rows}
        rowKey={(b) => b.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.isError ? list.error.message : null}
        onRetry={() => void list.refetch()}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
        emptyTitle={params.q || status ? t('NoBackupsMatch') : t('NoBackupsFound')}
        rowActionsLabel={t('Actions')}
        rowActions={(b) => (
          <>
            <IconButton icon="download" label={t('Download')} onClick={() => downloadBackup(b.id)} disabled={b.status !== 'Completed'} />
            {canManage && <IconButton icon="check" label={t('Validate')} onClick={() => void check(b)} />}
            {canManage && <IconButton icon="trash" tone="danger" label={t('Delete')} onClick={() => void askDelete(b)} />}
          </>
        )}
        toolbar={
          <div className={styles.toolbar}>
            <SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchBackups')} aria-label={t('SearchBackups')} />
            <ChipGroup
              aria-label={t('Status')}
              value={status}
              onChange={(next) => {
                setStatus(next)
                update({ page: 1 })
              }}
              options={STATUS_FILTERS.map((value) => ({ value, label: value ? t(`Status_${value}`) : t('Filter_AllStatuses') }))}
            />
            {selected.size > 0 && (
              <div className={styles.bulk} role="group" aria-label={t('SelectedCount', { 0: formatNumber(selected.size, 0) })}>
                <strong>{t('SelectedCount', { 0: formatNumber(selected.size, 0) })}</strong>
                {canManage && (
                  <Button onClick={() => void checkSelected()} loading={bulkValidate.isPending}>
                    {t('ValidateAll')}
                  </Button>
                )}
                {canManage && (
                  <Button variant="danger" onClick={() => void askBulkDelete()}>
                    {t('BulkDelete')}
                  </Button>
                )}
                <Button variant="ghost" onClick={() => setSelected(new Set())}>
                  {t('Cancel')}
                </Button>
              </div>
            )}
          </div>
        }
        footer={
          <Pagination
            page={params.page}
            pageSize={params.pageSize}
            total={list.data?.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={(page) => update({ page })}
            onPageSizeChange={(pageSize) => update({ pageSize })}
          />
        }
      />
    </div>
  )
}
