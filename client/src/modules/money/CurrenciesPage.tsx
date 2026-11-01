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
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { useCurrencies, useCurrencyOptions, useDeleteCurrency, useExchangeRateCount, useSetDefaultCurrency, type Currency } from './api'
import { CurrencyConverter } from './CurrencyConverter'
import { CurrencyFormModal } from './CurrencyFormModal'
import { MoneyLayout } from './MoneyLayout'
import styles from './money.module.css'

const DEFAULT_SORT = { key: 'code', dir: 'asc' } as const

/** `/app/currency` - currencies and the rate the till converts with; add / edit in a dialog, "make default", delete, and a converter to check a rate. */
export default function CurrenciesPage() {
  const { t } = useTranslation(['money', 'common'])
  const canCreate = useCan(Permissions.Currency_Create)
  const canEdit = useCan(Permissions.Currency_Edit)
  const canDelete = useCan(Permissions.Currency_Delete)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useCurrencies(params)
  const all = useCurrencyOptions().data?.items
  const rateCount = useExchangeRateCount().data
  const setDefault = useSetDefaultCurrency()
  const remove = useDeleteCurrency()
  const [editing, setEditing] = useState<Currency | 'new' | null>(null)

  const makeDefault = async (currency: Currency) => {
    try {
      await setDefault.mutateAsync(currency.id)
      toast.success(t('DefaultCurrencyChanged', { ns: 'money', code: currency.code }))
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('common.actionFailed', { ns: 'common' }))
    }
  }

  const askDelete = async (currency: Currency) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteCurrencyConfirm', { ns: 'money' }),
      detail: `${currency.code} - ${currency.name}`,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      // The default currency, or one that suppliers, receipts or rates still use, is refused (409) with the reason; it shows inside the dialog.
      onConfirm: async () => {
        await remove.mutateAsync(currency.id)
        toast.success(t('CurrencyDeleted', { ns: 'money' }))
      },
    })
  }

  const defaultCurrency = all?.find((c) => c.isDefault)
  const columns: Column<Currency>[] = [
    {
      key: 'code',
      header: t('CurrencyCode', { ns: 'money' }),
      sortable: true,
      required: true,
      mono: true,
      cell: (c) => <span className={styles.strong}>{c.code}</span>,
    },
    { key: 'name', header: t('CurrencyName', { ns: 'money' }), sortable: true, cell: (c) => c.name },
    { key: 'notation', header: t('CurrencySymbol', { ns: 'money' }), sortable: true, align: 'center', hideBelow: 700, cell: (c) => <bdi>{c.notation}</bdi> },
    {
      key: 'rateToUsd',
      header: t('RateToUsd', { ns: 'money' }),
      sortable: true,
      align: 'end',
      cell: (c) => <bdi>{formatQty(c.rateToUsd, 6)}</bdi>,
    },
    {
      key: 'isDefault',
      header: t('Default', { ns: 'money' }),
      sortable: true,
      align: 'center',
      cell: (c) => (c.isDefault ? <StatusBadge tone="success" label={t('Default', { ns: 'money' })} /> : <span className={styles.muted}>-</span>),
    },
    {
      key: 'isActive',
      header: t('Status', { ns: 'common' }),
      sortable: true,
      align: 'center',
      cell: (c) => <StatusBadge tone={c.isActive ? 'success' : 'neutral'} label={c.isActive ? t('Active', { ns: 'common' }) : t('Inactive', { ns: 'common' })} />,
    },
    { key: 'createdAt', header: t('Created', { ns: 'common' }), sortable: true, hideBelow: 1100, cell: (c) => <bdi>{formatDate(c.createdAt)}</bdi> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('CurrencyManagement', { ns: 'money' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('AddCurrency', { ns: 'money' }) : undefined}
        onPrimaryAction={canCreate ? () => setEditing('new') : undefined}
      />
      <div className={styles.tiles}>
        <StatTile label={t('ActiveCurrencies', { ns: 'money' })} value={all ? formatNumber(all.filter((c) => c.isActive).length, 0) : '-'} />
        <StatTile
          label={t('DefaultCurrency', { ns: 'money' })}
          value={defaultCurrency?.code ?? '-'}
          sub={defaultCurrency ? defaultCurrency.name : undefined}
          tone="success"
        />
        <StatTile label={t('PairRates', { ns: 'money' })} value={rateCount === undefined ? '-' : formatNumber(rateCount, 0)} sub={t('PairRatesSub', { ns: 'money' })} />
      </div>
      <MoneyLayout>
        <DataTable<Currency>
          caption={t('Currencies', { ns: 'money' })}
          columns={columns}
          rows={list.data?.items ?? []}
          rowKey={(c) => c.id}
          loading={list.isPending}
          refreshing={list.isFetching && !list.isPending}
          error={list.isError ? list.error.message : null}
          onRetry={() => void list.refetch()}
          sort={sort}
          onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
          emptyTitle={params.q ? t('NoCurrenciesMatch', { ns: 'money' }) : t('NoCurrencies', { ns: 'money' })}
          rowActionsLabel={t('Actions', { ns: 'common' })}
          rowActions={
            canEdit || canDelete
              ? (c) => (
                  <>
                    {canEdit && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(c)} />}
                    {canEdit && !c.isDefault && c.isActive && <IconButton icon="check" label={t('MakeDefault', { ns: 'money' })} onClick={() => void makeDefault(c)} />}
                    {canDelete && !c.isDefault && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(c)} />}
                  </>
                )
              : undefined
          }
          toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchCurrencies', { ns: 'money' })} aria-label={t('SearchCurrencies', { ns: 'money' })} />}
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
        <CurrencyConverter />
      </MoneyLayout>

      {editing && <CurrencyFormModal currency={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
