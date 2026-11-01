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
import { useDeletePaymentMethod, usePaymentMethods, type PaymentMethod } from './api'
import { MoneyLayout } from './MoneyLayout'
import styles from './money.module.css'
import { PaymentMethodFormModal } from './PaymentMethodFormModal'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

/** `/app/payment-methods` - the ways to pay at the till. "Customer Debt" is the system method behind "add to customer account": listed, never editable. */
export default function PaymentMethodsPage() {
  const { t } = useTranslation(['money', 'common'])
  const canManage = useCan(Permissions.PaymentMethods_Manage)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = usePaymentMethods(params)
  const remove = useDeletePaymentMethod()
  const [editing, setEditing] = useState<PaymentMethod | 'new' | null>(null)

  const askDelete = async (method: PaymentMethod) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeletePaymentMethodConfirm', { ns: 'money' }),
      detail: method.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      // A method that receipts were paid with is refused (409) with the reason; it shows inside the dialog.
      onConfirm: async () => {
        await remove.mutateAsync(method.id)
        toast.success(t('PaymentMethodDeleted', { ns: 'money' }))
      },
    })
  }

  const columns: Column<PaymentMethod>[] = [
    {
      key: 'name',
      header: t('PaymentMethodName', { ns: 'money' }),
      sortable: true,
      required: true,
      cell: (m) => (
        <div className={styles.badges}>
          <span className={styles.strong}>
            <bdi>{m.name}</bdi>
          </span>
          {m.isSystem && <StatusBadge tone="info" label={t('System', { ns: 'money' })} />}
        </div>
      ),
    },
    {
      key: 'givesChange',
      header: t('GivesChange', { ns: 'money' }),
      align: 'center',
      hideBelow: 800,
      cell: (m) => (m.givesChange ? <StatusBadge tone="neutral" label={t('Yes', { ns: 'common' })} /> : <span className={styles.muted}>-</span>),
    },
    {
      key: 'isDefault',
      header: t('Default', { ns: 'money' }),
      sortable: true,
      align: 'center',
      cell: (m) => (m.isDefault ? <StatusBadge tone="success" label={t('Default', { ns: 'money' })} /> : <span className={styles.muted}>-</span>),
    },
    { key: 'createdAt', header: t('Created', { ns: 'common' }), sortable: true, hideBelow: 900, cell: (m) => <bdi>{formatDate(m.createdAt)}</bdi> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('PaymentMethodManagement', { ns: 'money' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canManage ? t('AddPaymentMethod', { ns: 'money' }) : undefined}
        onPrimaryAction={canManage ? () => setEditing('new') : undefined}
      />
      <MoneyLayout>
        <p className={styles.note}>{t('PaymentMethodsNote', { ns: 'money' })}</p>
        <DataTable<PaymentMethod>
          caption={t('PaymentMethods', { ns: 'money' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(m) => m.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoPaymentMethodsMatch', { ns: 'money' }) : t('NoPaymentMethods', { ns: 'money' })}
          rowActionsLabel={t('Actions', { ns: 'common' })}
          rowActions={
            canManage
              ? (m) =>
                  m.isSystem ? null : (
                    <>
                      <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(m)} />
                      <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(m)} />
                    </>
                  )
              : undefined
          }
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchPaymentMethods', { ns: 'money' })} aria-label={t('SearchPaymentMethods', { ns: 'money' })} />}
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

      {editing && <PaymentMethodFormModal method={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
