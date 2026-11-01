import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useSession } from '@/core/auth/useSession'
import { useCan } from '@/core/permissions/permissions'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDate, formatDateTime, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { useDeleteUser, useSetUserActive, useUsers, type UserRow } from './api'
import { ResetPinModal } from './ResetPinModal'
import { UserFormModal } from './UserFormModal'
import { UsersLayout } from './UsersLayout'
import styles from './users.module.css'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

/** `/app/users` - the users table with create / edit in a dialog, enable / disable, PIN reset and delete. */
export default function UsersPage() {
  const { t } = useTranslation(['users', 'common'])
  const me = useSession().user.id
  const canCreate = useCan(Permissions.Users_Create)
  const canEdit = useCan(Permissions.Users_Edit)
  const canDelete = useCan(Permissions.Users_Delete)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useUsers(params)
  const setActive = useSetUserActive()
  const remove = useDeleteUser()
  const [editing, setEditing] = useState<UserRow | 'new' | null>(null)
  const [resetFor, setResetFor] = useState<UserRow | null>(null)

  const toggle = async (user: UserRow) => {
    const enable = !user.isActive
    try {
      await setActive.mutateAsync({ id: user.id, isActive: enable })
      toast.success(enable ? t('UserEnabled', { ns: 'users' }) : t('UserDisabled', { ns: 'users' }))
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('common.actionFailed', { ns: 'common' }))
    }
  }

  const askDelete = async (user: UserRow) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteUserConfirm', { ns: 'users' }),
      detail: user.fullName,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      // A user with sales, receipts or sessions on record is refused (409 user_in_use) with the reason; it shows inside the dialog.
      onConfirm: async () => {
        await remove.mutateAsync(user.id)
        toast.success(t('UserDeleted', { ns: 'users' }))
      },
    })
  }

  const columns: Column<UserRow>[] = [
    {
      key: 'name',
      header: t('User', { ns: 'users' }),
      sortable: true,
      required: true,
      cell: (u) => (
        <div className={styles.person}>
          <span className={styles.personName}>
            <bdi>{u.fullName}</bdi>
          </span>
          <span className={styles.muted}>
            <bdi>{u.username}</bdi>
          </span>
        </div>
      ),
    },
    {
      key: 'email',
      header: t('Email', { ns: 'users' }),
      sortable: true,
      hideBelow: 900,
      cell: (u) => <bdi className={styles.clip} title={u.email}>{u.email}</bdi>,
    },
    { key: 'role', header: t('Role', { ns: 'users' }), sortable: true, cell: (u) => <StatusBadge tone="info" label={u.roleName} /> },
    {
      key: 'isActive',
      header: t('Status', { ns: 'users' }),
      sortable: true,
      align: 'center',
      cell: (u) => (
        <div className={styles.badges}>
          <StatusBadge tone={u.isActive ? 'success' : 'neutral'} label={u.isActive ? t('Active', { ns: 'common' }) : t('Inactive', { ns: 'common' })} />
          {u.mustChangePin && <StatusBadge tone="warning" label={t('MustChangePin', { ns: 'users' })} />}
        </div>
      ),
    },
    {
      key: 'lastLoginAt',
      header: t('LastLogin', { ns: 'users' }),
      sortable: true,
      hideBelow: 1000,
      cell: (u) => (u.lastLoginAt ? <bdi>{formatDateTime(u.lastLoginAt)}</bdi> : <span className={styles.muted}>{t('Never', { ns: 'common' })}</span>),
    },
    { key: 'createdAt', header: t('Created', { ns: 'users' }), sortable: true, hideBelow: 1200, cell: (u) => <bdi>{formatDate(u.createdAt)}</bdi> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('UserManagement', { ns: 'users' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('AddUser', { ns: 'users' }) : undefined}
        onPrimaryAction={canCreate ? () => setEditing('new') : undefined}
      />
      <UsersLayout>
        <DataTable<UserRow>
          caption={t('Users', { ns: 'users' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(u) => u.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoUsersMatch', { ns: 'users' }) : t('NoUsers', { ns: 'users' })}
          rowActionsLabel={t('Actions', { ns: 'users' })}
          rowActions={
            canEdit || canDelete
              ? (u) => (
                  <>
                    {canEdit && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(u)} />}
                    {/* Disabling or deleting yourself would end your own session on the next request. */}
                    {canEdit && u.id !== me && (
                      <IconButton
                        icon={u.isActive ? 'close' : 'check'}
                        label={u.isActive ? t('Disable', { ns: 'users' }) : t('Enable', { ns: 'users' })}
                        onClick={() => void toggle(u)}
                      />
                    )}
                    {canDelete && u.id !== me && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(u)} />}
                  </>
                )
              : undefined
          }
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchUsers', { ns: 'users' })} aria-label={t('SearchUsers', { ns: 'users' })} />}
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
      </UsersLayout>

      {editing && (
        <UserFormModal
          user={editing === 'new' ? undefined : editing}
          onClose={() => setEditing(null)}
          onResetPin={
            canEdit && editing !== 'new'
              ? () => {
                  setResetFor(editing)
                  setEditing(null)
                }
              : undefined
          }
        />
      )}
      {resetFor && <ResetPinModal user={resetFor} onClose={() => setResetFor(null)} />}
    </div>
  )
}
