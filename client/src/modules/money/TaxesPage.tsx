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
import { useDeactivateTax, useDeleteTax, useSetDefaultTax, useTaxes, type TaxRate } from './api'
import { MoneyLayout } from './MoneyLayout'
import styles from './money.module.css'
import { TaxCalculator } from './TaxCalculator'
import { dayOf } from './taxDates'
import { TAX_TYPES, TaxFormModal } from './TaxFormModal'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

/** `/app/tax` - tax rates: add / edit in a dialog, make default, deactivate (ends it today), delete, and a calculator. */
export default function TaxesPage() {
  const { t } = useTranslation(['money', 'common'])
  const canCreate = useCan(Permissions.Tax_Create)
  const canEdit = useCan(Permissions.Tax_Edit)
  const canDelete = useCan(Permissions.Tax_Delete)
  const canManage = useCan(Permissions.Tax_Manage)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useTaxes(params)
  const setDefault = useSetDefaultTax()
  const deactivate = useDeactivateTax()
  const remove = useDeleteTax()
  const [editing, setEditing] = useState<TaxRate | 'new' | null>(null)

  const askDefault = async (tax: TaxRate) => {
    await confirm({
      title: t('MakeDefault', { ns: 'money' }),
      message: t('MakeDefaultTaxConfirm', { ns: 'money' }),
      detail: tax.name,
      confirmLabel: t('MakeDefault', { ns: 'money' }),
      onConfirm: async () => {
        await setDefault.mutateAsync(tax.id)
        toast.success(t('DefaultTaxChanged', { ns: 'money' }))
      },
    })
  }

  const askDeactivate = async (tax: TaxRate) => {
    await confirm({
      title: t('Deactivate', { ns: 'money' }),
      message: t('DeactivateTaxConfirm', { ns: 'money' }),
      detail: tax.name,
      confirmLabel: t('Deactivate', { ns: 'money' }),
      danger: true,
      onConfirm: async () => {
        await deactivate.mutateAsync(tax.id)
        toast.success(t('TaxDeactivated', { ns: 'money' }))
      },
    })
  }

  const askDelete = async (tax: TaxRate) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteTaxConfirm', { ns: 'money' }),
      detail: tax.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(tax.id)
        toast.success(t('TaxDeleted', { ns: 'money' }))
      },
    })
  }

  const typeLabel = (type: number) => {
    const known = TAX_TYPES.find((x) => x.value === type)
    return known ? t(known.key, { ns: 'money' }) : t('TaxTypeCustom', { ns: 'money' })
  }
  const date = (instant: string | null | undefined) => (instant ? <bdi>{formatDate(dayOf(instant))}</bdi> : <span className={styles.muted}>-</span>)

  const columns: Column<TaxRate>[] = [
    {
      key: 'name',
      header: t('TaxName', { ns: 'money' }),
      sortable: true,
      required: true,
      cell: (x) => (
        <div>
          <span className={styles.strong}>
            <bdi>{x.name}</bdi>
          </span>
          {x.taxCode && (
            <div className={styles.muted}>
              <bdi>{x.taxCode}</bdi>
            </div>
          )}
        </div>
      ),
    },
    { key: 'rate', header: t('TaxRateLabel', { ns: 'money' }), sortable: true, align: 'end', cell: (x) => formatPercent(x.rate, 2) },
    { key: 'type', header: t('Type', { ns: 'common' }), sortable: true, hideBelow: 800, cell: (x) => typeLabel(x.type) },
    {
      key: 'isDefault',
      header: t('Default', { ns: 'money' }),
      sortable: true,
      align: 'center',
      cell: (x) => (x.isDefault ? <StatusBadge tone="success" label={t('Default', { ns: 'money' })} /> : <span className={styles.muted}>-</span>),
    },
    {
      key: 'isActive',
      header: t('Status', { ns: 'common' }),
      sortable: true,
      align: 'center',
      cell: (x) => (
        <div className={styles.badges}>
          <StatusBadge tone={x.isActive ? 'success' : 'neutral'} label={x.isActive ? t('Active', { ns: 'common' }) : t('Inactive', { ns: 'common' })} />
          {x.isInclusive && <StatusBadge tone="info" label={t('Inclusive', { ns: 'money' })} />}
        </div>
      ),
    },
    { key: 'effectiveFrom', header: t('EffectiveFrom', { ns: 'money' }), sortable: true, hideBelow: 1000, cell: (x) => date(x.effectiveFrom) },
    { key: 'effectiveTo', header: t('EffectiveTo', { ns: 'money' }), sortable: true, hideBelow: 1000, cell: (x) => date(x.effectiveTo) },
    {
      key: 'category',
      header: t('Category', { ns: 'money' }),
      sortable: true,
      hideBelow: 1200,
      cell: (x) => (x.categoryName ? <bdi>{x.categoryName}</bdi> : <span className={styles.muted}>{t('AllCategories', { ns: 'money' })}</span>),
    },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('TaxManagement', { ns: 'money' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('AddTax', { ns: 'money' }) : undefined}
        onPrimaryAction={canCreate ? () => setEditing('new') : undefined}
      />
      <MoneyLayout>
        <p className={styles.note}>{t('TaxNote', { ns: 'money' })}</p>
        <DataTable<TaxRate>
          caption={t('Taxes', { ns: 'money' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(x) => x.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoTaxesMatch', { ns: 'money' }) : t('NoTaxes', { ns: 'money' })}
          rowActionsLabel={t('Actions', { ns: 'common' })}
          rowActions={
            canEdit || canDelete || canManage
              ? (x) => (
                  <>
                    {canEdit && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(x)} />}
                    {canManage && !x.isDefault && <IconButton icon="check" label={t('MakeDefault', { ns: 'money' })} onClick={() => void askDefault(x)} />}
                    {canManage && x.isActive && <IconButton icon="minus" label={t('Deactivate', { ns: 'money' })} onClick={() => void askDeactivate(x)} />}
                    {canDelete && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(x)} />}
                  </>
                )
              : undefined
          }
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchTaxes', { ns: 'money' })} aria-label={t('SearchTaxes', { ns: 'money' })} />}
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
        <TaxCalculator />
      </MoneyLayout>

      {editing && <TaxFormModal tax={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
