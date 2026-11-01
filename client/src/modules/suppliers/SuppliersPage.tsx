import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
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
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { SupplierDebtDialog } from './SupplierDebtDialog'
import { SupplierFormModal } from './SupplierFormModal'
import { balanceSign, downloadSuppliersCsv, useDeleteSupplier, useSupplierSummary, useSuppliers, type Supplier } from './api'
import styles from './suppliers.module.css'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

/** `/app/suppliers` - the list, the tiles, create / edit in a dialog, debt entries, delete, CSV export. */
export default function SuppliersPage() {
  const { t } = useTranslation(['suppliers', 'common'])
  const navigate = useNavigate()
  const canCreate = useCan(Permissions.Suppliers_Create)
  const canEdit = useCan(Permissions.Suppliers_Edit)
  const canDelete = useCan(Permissions.Suppliers_Delete)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useSuppliers(params)
  const summary = useSupplierSummary()
  const remove = useDeleteSupplier()
  const [editing, setEditing] = useState<Supplier | 'new' | null>(null)
  const [debtFor, setDebtFor] = useState<Supplier | null>(null)
  const [exporting, setExporting] = useState(false)

  const askDelete = async (supplier: Supplier) => {
    await confirm({
      title: t('ConfirmTitle'),
      message: t('DeleteConfirmText'),
      detail: supplier.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      // The server's refusals (balance outstanding, still used by products) arrive already localized and show in the dialog.
      onConfirm: async () => {
        await remove.mutateAsync(supplier.id)
        toast.success(t('SupplierDeleted'))
      },
    })
  }

  const exportCsv = async () => {
    setExporting(true)
    try {
      await downloadSuppliersCsv()
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('common.actionFailed', { ns: 'common' }))
    } finally {
      setExporting(false)
    }
  }

  const notProvided = <span className={styles.muted}>{t('NotProvided')}</span>

  const columns: Column<Supplier>[] = [
    {
      key: 'name',
      header: t('Name'),
      sortable: true,
      required: true,
      cell: (s) => (
        <Link className={styles.name} to={`/suppliers/${s.id}`}>
          <bdi>{s.name}</bdi>
        </Link>
      ),
    },
    {
      key: 'email',
      header: t('Email'),
      sortable: true,
      hideBelow: 1000,
      cell: (s) => (s.email ? <a className={styles.link} href={`mailto:${s.email}`}><bdi>{s.email}</bdi></a> : notProvided),
    },
    {
      key: 'phone',
      header: t('Phone'),
      sortable: true,
      hideBelow: 800,
      cell: (s) => (s.phone ? <a className={styles.link} href={`tel:${s.phone}`}><bdi>{s.phone}</bdi></a> : notProvided),
    },
    {
      key: 'address',
      header: t('Address'),
      hideBelow: 1200,
      cell: (s) => (s.address ? <bdi className={styles.clip} title={s.address}>{s.address}</bdi> : notProvided),
    },
    {
      key: 'balance',
      header: t('Balance'),
      sortable: true,
      align: 'end',
      cell: (s) => (
        <span className={styles.balance} data-sign={balanceSign(s.balance)}>
          {formatMoney(s.balance, s.currencyNotation)}
        </span>
      ),
    },
    {
      key: 'isActive',
      header: t('Status'),
      sortable: true,
      align: 'center',
      cell: (s) => <StatusBadge tone={s.isActive ? 'success' : 'neutral'} label={s.isActive ? t('Active') : t('Inactive')} />,
    },
  ]

  const stats = summary.data
  const tile = (value: number | undefined) => (value === undefined ? '-' : formatNumber(value, 0))

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('Suppliers')}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('AddSupplier') : undefined}
        onPrimaryAction={canCreate ? () => setEditing('new') : undefined}
      >
        <Button onClick={() => void exportCsv()} loading={exporting}>
          {t('ExportSuppliers')}
        </Button>
      </PageHeader>

      <div className={styles.tiles}>
        <StatTile label={t('TotalSuppliers')} value={tile(stats?.total)} />
        <StatTile label={t('ActiveSuppliers')} value={tile(stats?.active)} tone="success" />
        <StatTile label={t('InactiveSuppliers')} value={tile(stats?.inactive)} />
        <StatTile label={t('SuppliersWithBalance')} value={tile(stats?.withBalance)} tone="danger" />
      </div>

      <DataTable<Supplier>
        caption={t('SuppliersList')}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(s) => s.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.isError ? list.error.message : null}
        onRetry={() => void list.refetch()}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
        onRowClick={(s) => void navigate(`/suppliers/${s.id}`)}
        emptyTitle={params.q ? t('NoSuppliersMatch') : t('NoSuppliers')}
        emptyMessage={params.q ? undefined : t('NoSuppliersMessage')}
        rowActionsLabel={t('Actions')}
        rowActions={
          canEdit || canDelete
            ? (s) => (
                <>
                  {canEdit && <IconButton icon="plus" label={t('AddTransaction')} onClick={() => setDebtFor(s)} />}
                  {canEdit && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(s)} />}
                  {canDelete && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(s)} />}
                </>
              )
            : undefined
        }
        toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchSuppliers')} aria-label={t('SearchSuppliers')} />}
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

      {editing && <SupplierFormModal supplier={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
      {debtFor && <SupplierDebtDialog supplier={debtFor} onClose={() => setDebtFor(null)} />}
    </div>
  )
}
