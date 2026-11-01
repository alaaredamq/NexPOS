import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDate, formatNumber, formatPercent } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { useDeleteDiscount, useDiscounts, type Discount } from './api'
import { DiscountFormModal } from './DiscountFormModal'
import { MoneyLayout } from './MoneyLayout'
import styles from './money.module.css'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

/** `/app/discounts` - the percentage discounts a cashier can pick at the till. */
export default function DiscountsPage() {
  const { t } = useTranslation(['money', 'common'])
  const canManage = useCan(Permissions.Discounts_Manage)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useDiscounts(params)
  const remove = useDeleteDiscount()
  const [editing, setEditing] = useState<Discount | 'new' | null>(null)

  const askDelete = async (discount: Discount) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteDiscountConfirm', { ns: 'money' }),
      detail: discount.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(discount.id)
        toast.success(t('DiscountDeleted', { ns: 'money' }))
      },
    })
  }

  const columns: Column<Discount>[] = [
    { key: 'name', header: t('DiscountName', { ns: 'money' }), sortable: true, required: true, cell: (d) => <span className={styles.strong}><bdi>{d.name}</bdi></span> },
    { key: 'rate', header: t('DiscountPercent', { ns: 'money' }), sortable: true, align: 'end', cell: (d) => formatPercent(d.rate, 2) },
    {
      key: 'isDefault',
      header: t('Default', { ns: 'money' }),
      sortable: true,
      align: 'center',
      cell: (d) => (d.isDefault ? <StatusBadge tone="success" label={t('Default', { ns: 'money' })} /> : <span className={styles.muted}>-</span>),
    },
    { key: 'createdAt', header: t('Created', { ns: 'common' }), sortable: true, hideBelow: 900, cell: (d) => <bdi>{formatDate(d.createdAt)}</bdi> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('DiscountManagement', { ns: 'money' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canManage ? t('AddDiscount', { ns: 'money' }) : undefined}
        onPrimaryAction={canManage ? () => setEditing('new') : undefined}
      />
      <MoneyLayout>
        <DataTable<Discount>
          caption={t('Discounts', { ns: 'money' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(d) => d.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoDiscountsMatch', { ns: 'money' }) : t('NoDiscounts', { ns: 'money' })}
          rowActionsLabel={t('Actions', { ns: 'common' })}
          rowActions={
            canManage
              ? (d) => (
                  <>
                    <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(d)} />
                    <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(d)} />
                  </>
                )
              : undefined
          }
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchDiscounts', { ns: 'money' })} aria-label={t('SearchDiscounts', { ns: 'money' })} />}
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

      {editing && <DiscountFormModal discount={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
