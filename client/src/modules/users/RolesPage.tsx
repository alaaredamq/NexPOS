import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDate, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { useDeleteRole, useRoles, type RoleRow } from './api'
import { RoleEditorModal } from './RoleEditorModal'
import { UsersLayout } from './UsersLayout'
import styles from './users.module.css'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

/** `/app/users/roles` - roles and their permission counts; the permission matrix opens in a dialog. Everything that shows or changes grants needs `Users.Manage`. */
export default function RolesPage() {
  const { t } = useTranslation(['users', 'common'])
  const canManage = useCan(Permissions.Users_Manage)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useRoles(params)
  const remove = useDeleteRole()
  const [editing, setEditing] = useState<RoleRow | 'new' | null>(null)

  const askDelete = async (role: RoleRow) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteRoleConfirm', { ns: 'users' }),
      detail: role.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      // A built-in role, or one users still hold, is refused (409) with the reason; it shows inside the dialog.
      onConfirm: async () => {
        await remove.mutateAsync(role.id)
        toast.success(t('RoleDeleted', { ns: 'users' }))
      },
    })
  }

  const columns: Column<RoleRow>[] = [
    {
      key: 'name',
      header: t('RoleName', { ns: 'users' }),
      sortable: true,
      required: true,
      cell: (r) => (
        <div className={styles.badges}>
          <span className={styles.personName}>
            <bdi>{r.name}</bdi>
          </span>
          {r.isSystemRole && <StatusBadge tone="ink" label={t('SystemRole', { ns: 'users' })} />}
        </div>
      ),
    },
    {
      key: 'description',
      header: t('Description', { ns: 'users' }),
      hideBelow: 900,
      cell: (r) => (r.description ? <span className={styles.clip} title={r.description}>{r.description}</span> : <span className={styles.muted}>-</span>),
    },
    { key: 'userCount', header: t('Users', { ns: 'users' }), sortable: true, align: 'center', cell: (r) => formatNumber(r.userCount, 0) },
    { key: 'permissionCount', header: t('Permissions', { ns: 'users' }), sortable: true, align: 'center', cell: (r) => formatNumber(r.permissionCount, 0) },
    { key: 'createdAt', header: t('Created', { ns: 'users' }), sortable: true, hideBelow: 1000, cell: (r) => <bdi>{formatDate(r.createdAt)}</bdi> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('UserManagement', { ns: 'users' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canManage ? t('AddRole', { ns: 'users' }) : undefined}
        onPrimaryAction={canManage ? () => setEditing('new') : undefined}
      />
      <UsersLayout>
        <DataTable<RoleRow>
          caption={t('Roles', { ns: 'users' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(r) => r.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoRolesMatch', { ns: 'users' }) : t('NoRoles', { ns: 'users' })}
          rowActionsLabel={t('Actions', { ns: 'users' })}
          rowActions={
            canManage
              ? (r) => (
                  <>
                    <IconButton icon="edit" label={r.isSuperAdmin ? t('ViewRole', { ns: 'users' }) : t('Edit', { ns: 'common' })} onClick={() => setEditing(r)} />
                    {!r.isSystemRole && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(r)} />}
                  </>
                )
              : undefined
          }
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchRoles', { ns: 'users' })} aria-label={t('SearchRoles', { ns: 'users' })} />}
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

      {editing && <RoleEditorModal roleId={editing === 'new' ? undefined : editing.id} onClose={() => setEditing(null)} />}
    </div>
  )
}
