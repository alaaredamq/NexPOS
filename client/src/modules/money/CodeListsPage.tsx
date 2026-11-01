import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { useCodeLists, useDeleteCodeList, type CodeList } from './api'
import { CodeListFormModal } from './CodeListFormModal'
import { CodeListValuesModal } from './CodeListValuesModal'
import { MoneyLayout } from './MoneyLayout'
import styles from './money.module.css'

const DEFAULT_SORT = { key: 'displayOrder', dir: 'asc' } as const

/** `/app/code-lists` - named sets of values; the values open in a dialog. Reading needs Settings.View (or Manage), changing needs Settings.Manage. */
export default function CodeListsPage() {
  const { t } = useTranslation(['money', 'common'])
  const canManage = useCan(Permissions.Settings_Manage)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useCodeLists(params)
  const remove = useDeleteCodeList()
  const [editing, setEditing] = useState<CodeList | 'new' | null>(null)
  const [valuesOf, setValuesOf] = useState<CodeList | null>(null)

  const askDelete = async (codeList: CodeList) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteCodeListConfirm', { ns: 'money' }),
      detail: codeList.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(codeList.id)
        toast.success(t('CodeListDeleted', { ns: 'money' }))
      },
    })
  }

  const columns: Column<CodeList>[] = [
    { key: 'key', header: t('ListKey', { ns: 'money' }), sortable: true, required: true, mono: true, cell: (l) => <span className={styles.strong}>{l.key}</span> },
    { key: 'name', header: t('ListName', { ns: 'money' }), sortable: true, cell: (l) => <bdi>{l.name}</bdi> },
    { key: 'valueCount', header: t('Values', { ns: 'money' }), sortable: true, align: 'end', cell: (l) => <bdi>{formatNumber(l.valueCount, 0)}</bdi> },
    { key: 'displayOrder', header: t('DisplayOrder', { ns: 'money' }), sortable: true, align: 'end', hideBelow: 800, cell: (l) => <bdi>{l.displayOrder}</bdi> },
    {
      key: 'isActive',
      header: t('Status', { ns: 'common' }),
      sortable: true,
      align: 'center',
      cell: (l) => <StatusBadge tone={l.isActive ? 'success' : 'neutral'} label={l.isActive ? t('Active', { ns: 'common' }) : t('Inactive', { ns: 'common' })} />,
    },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('CodeLists', { ns: 'money' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canManage ? t('AddCodeList', { ns: 'money' }) : undefined}
        onPrimaryAction={canManage ? () => setEditing('new') : undefined}
      />
      <MoneyLayout>
        <p className={styles.note}>{t('CodeListsNote', { ns: 'money' })}</p>
        <DataTable<CodeList>
          caption={t('CodeLists', { ns: 'money' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(l) => l.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoCodeListsMatch', { ns: 'money' }) : t('NoCodeLists', { ns: 'money' })}
          rowActionsLabel={t('Actions', { ns: 'common' })}
          rowActions={(l) => (
            <>
              <IconButton icon="columns" label={t('ManageValues', { ns: 'money' })} onClick={() => setValuesOf(l)} />
              {canManage && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(l)} />}
              {canManage && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(l)} />}
            </>
          )}
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchCodeLists', { ns: 'money' })} aria-label={t('SearchCodeLists', { ns: 'money' })} />}
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
      </MoneyLayout>

      {editing && <CodeListFormModal list={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      {valuesOf && <CodeListValuesModal list={valuesOf} canManage={canManage} onClose={() => setValuesOf(null)} />}
    </div>
  )
}
