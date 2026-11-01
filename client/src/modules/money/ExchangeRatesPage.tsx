import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDate, formatNumber, formatQty } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { toast } from '@/ui/toast'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { useDeleteExchangeRate, useExchangeRates, type ExchangeRate } from './api'
import { ExchangeRateFormModal } from './ExchangeRateFormModal'
import { MoneyLayout } from './MoneyLayout'
import styles from './money.module.css'

const DEFAULT_SORT = { key: 'from', dir: 'asc' } as const

/** `/app/currency/exchange-rates` - pair-specific rates that override the USD bridge, optionally for one supplier. */
export default function ExchangeRatesPage() {
  const { t } = useTranslation(['money', 'common'])
  const canCreate = useCan(Permissions.Currency_Create)
  const canEdit = useCan(Permissions.Currency_Edit)
  const canDelete = useCan(Permissions.Currency_Delete)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useExchangeRates(params)
  const remove = useDeleteExchangeRate()
  const [editing, setEditing] = useState<ExchangeRate | 'new' | null>(null)

  const askDelete = async (rate: ExchangeRate) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteRateConfirm', { ns: 'money' }),
      detail: `${rate.fromCurrencyCode} / ${rate.toCurrencyCode}`,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(rate.id)
        toast.success(t('RateDeleted', { ns: 'money' }))
      },
    })
  }

  const columns: Column<ExchangeRate>[] = [
    { key: 'from', header: t('FromCurrency', { ns: 'money' }), sortable: true, required: true, mono: true, cell: (r) => <span className={styles.strong}>{r.fromCurrencyCode}</span> },
    { key: 'to', header: t('ToCurrency', { ns: 'money' }), sortable: true, mono: true, cell: (r) => <span className={styles.strong}>{r.toCurrencyCode}</span> },
    { key: 'rate', header: t('Rate', { ns: 'money' }), sortable: true, align: 'end', cell: (r) => <bdi>{formatQty(r.rate, 6)}</bdi> },
    {
      key: 'supplier',
      header: t('Supplier', { ns: 'money' }),
      sortable: true,
      cell: (r) => (r.supplierName ? <bdi>{r.supplierName}</bdi> : <span className={styles.muted}>{t('StoreWideRate', { ns: 'money' })}</span>),
    },
    { key: 'createdAt', header: t('Created', { ns: 'common' }), sortable: true, hideBelow: 1000, cell: (r) => <bdi>{formatDate(r.createdAt)}</bdi> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('ExchangeRates', { ns: 'money' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('AddExchangeRate', { ns: 'money' }) : undefined}
        onPrimaryAction={canCreate ? () => setEditing('new') : undefined}
      />
      <MoneyLayout>
        <p className={styles.note}>{t('ExchangeRatesNote', { ns: 'money' })}</p>
        <DataTable<ExchangeRate>
          caption={t('ExchangeRates', { ns: 'money' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(r) => r.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoRatesMatch', { ns: 'money' }) : t('NoRates', { ns: 'money' })}
          emptyMessage={params.q ? undefined : t('NoRatesMessage', { ns: 'money' })}
          rowActionsLabel={t('Actions', { ns: 'common' })}
          rowActions={
            canEdit || canDelete
              ? (r) => (
                  <>
                    {canEdit && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(r)} />}
                    {canDelete && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(r)} />}
                  </>
                )
              : undefined
          }
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchRates', { ns: 'money' })} aria-label={t('SearchRates', { ns: 'money' })} />}
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

      {editing && <ExchangeRateFormModal rate={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
