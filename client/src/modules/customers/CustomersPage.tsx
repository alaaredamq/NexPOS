import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { saveDownload } from '../_shared/saveDownload'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatMoney, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { customersApi, useCustomers, useCustomerSummary, useDeleteCustomer, type Customer } from './api'
import { Balance } from './Balance'
import { CustomerDebtDialog } from './CustomerDebtDialog'
import { CustomerFormModal } from './CustomerFormModal'
import { PAGE_SIZES, useListParams } from './useListParams'
import styles from './customers.module.css'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

const fullName = (c: Pick<Customer, 'firstName' | 'lastName'>) => `${c.firstName} ${c.lastName}`.trim()

/** `/app/customers` - the list, the four tiles, create / edit in a dialog, debt entries, delete, CSV export. */
export default function CustomersPage() {
  const { t } = useTranslation(['customers', 'common'])
  const navigate = useNavigate()
  const canCreate = useCan(Permissions.Customers_Create)
  const canEdit = useCan(Permissions.Customers_Edit)
  const canDelete = useCan(Permissions.Customers_Delete)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useCustomers(params)
  const summary = useCustomerSummary()
  const remove = useDeleteCustomer()
  const [editing, setEditing] = useState<Customer | 'new' | null>(null)
  const [debtFor, setDebtFor] = useState<Customer | null>(null)
  const [exporting, setExporting] = useState(false)

  const notation = summary.data?.currencyNotation ?? ''

  const askDelete = async (customer: Customer) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('AreYouSureYouWantToDeleteThisCustomer', { ns: 'customers' }),
      detail: fullName(customer),
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      // A customer with a balance is refused by the server (409 customer_has_balance) with a localized sentence; it shows inside the dialog.
      onConfirm: async () => {
        await remove.mutateAsync(customer.id)
        toast.success(t('CustomerDeleted', { ns: 'customers' }))
      },
    })
  }

  const exportCsv = async () => {
    setExporting(true)
    try {
      saveDownload(await customersApi.export(), 'customers.csv')
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('common.actionFailed', { ns: 'common' }))
    } finally {
      setExporting(false)
    }
  }

  const columns: Column<Customer>[] = [
    {
      key: 'name',
      header: t('Name', { ns: 'customers' }),
      sortable: true,
      required: true,
      cell: (c) => (
        <Link className={styles.name} to={`/customers/${c.id}`}>
          <bdi>{fullName(c)}</bdi>
        </Link>
      ),
    },
    {
      key: 'email',
      header: t('Email', { ns: 'customers' }),
      sortable: true,
      hideBelow: 1000,
      cell: (c) => (c.email ? <bdi className={styles.clip} title={c.email}>{c.email}</bdi> : <span className={styles.muted}>-</span>),
    },
    {
      key: 'phoneNumber',
      header: t('Phone', { ns: 'customers' }),
      sortable: true,
      mono: true,
      cell: (c) => (c.phoneNumber ? <bdi>{c.phoneNumber}</bdi> : <span className={styles.muted}>-</span>),
    },
    {
      key: 'address',
      header: t('Address', { ns: 'customers' }),
      sortable: true,
      hideBelow: 1200,
      cell: (c) => (c.address ? <bdi className={styles.clip} title={c.address}>{c.address}</bdi> : <span className={styles.muted}>{t('NoAddress', { ns: 'customers' })}</span>),
    },
    { key: 'ordersCount', header: t('Orders', { ns: 'customers' }), sortable: true, align: 'center', hideBelow: 800, cell: (c) => formatNumber(c.ordersCount, 0) },
    { key: 'totalSpent', header: t('TotalSpent', { ns: 'customers' }), sortable: true, align: 'end', hideBelow: 900, cell: (c) => formatMoney(c.totalSpent, notation) },
    { key: 'balance', header: t('Balance', { ns: 'customers' }), sortable: true, align: 'end', cell: (c) => <Balance amount={c.balance} notation={notation} /> },
    {
      key: 'isActive',
      header: t('Status', { ns: 'customers' }),
      sortable: true,
      align: 'center',
      hideBelow: 700,
      cell: (c) => <StatusBadge tone={c.isActive ? 'success' : 'neutral'} label={c.isActive ? t('Active', { ns: 'customers' }) : t('Inactive', { ns: 'customers' })} />,
    },
  ]

  const stats = summary.data
  const tile = (value: string | undefined) => value ?? '-'

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('Customers', { ns: 'customers' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('AddCustomer', { ns: 'customers' }) : undefined}
        onPrimaryAction={canCreate ? () => setEditing('new') : undefined}
      >
        <Button onClick={() => void exportCsv()} loading={exporting}>
          {t('ExportCustomers', { ns: 'customers' })}
        </Button>
      </PageHeader>

      <div className={styles.tiles}>
        <StatTile label={t('TotalCustomers', { ns: 'customers' })} value={tile(stats && formatNumber(stats.total, 0))} />
        <StatTile label={t('ActiveCustomers', { ns: 'customers' })} value={tile(stats && formatNumber(stats.active, 0))} tone="success" />
        <StatTile
          label={t('OutstandingDebt', { ns: 'customers' })}
          value={tile(stats && formatMoney(stats.totalOwed, notation))}
          sub={stats ? t('CustomersOwing', { ns: 'customers', 0: formatNumber(stats.owing, 0) }) : undefined}
          tone={stats && stats.totalOwed > 0 ? 'danger' : 'default'}
        />
        <StatTile label={t('TotalRevenue', { ns: 'customers' })} value={tile(stats && formatMoney(stats.totalSpent, notation))} sub={t('TotalSales', { ns: 'customers' })} />
      </div>

      <DataTable<Customer>
        caption={t('CustomersList', { ns: 'customers' })}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(c) => c.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.isError ? list.error.message : null}
        onRetry={() => void list.refetch()}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
        onRowClick={(c) => void navigate(`/customers/${c.id}`)}
        emptyTitle={params.q ? t('NoCustomersMatch', { ns: 'customers' }) : t('NoCustomers', { ns: 'customers' })}
        emptyMessage={params.q ? undefined : t('NoCustomersMessage', { ns: 'customers' })}
        rowActionsLabel={t('Actions', { ns: 'customers' })}
        rowActions={
          canEdit || canDelete
            ? (c) => (
                <>
                  {canEdit && <IconButton icon="plus" label={t('AddDebtTransaction', { ns: 'customers' })} onClick={() => setDebtFor(c)} />}
                  {canEdit && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(c)} />}
                  {canDelete && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(c)} />}
                </>
              )
            : undefined
        }
        toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchCustomers', { ns: 'customers' })} aria-label={t('SearchCustomers', { ns: 'customers' })} />}
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

      {editing && <CustomerFormModal customer={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      {debtFor && <CustomerDebtDialog customer={{ id: debtFor.id, name: fullName(debtFor), balance: debtFor.balance }} currencyNotation={notation || undefined} onClose={() => setDebtFor(null)} />}
    </div>
  )
}
