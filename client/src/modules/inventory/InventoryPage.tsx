import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatMoney, formatNumber, formatQty } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { AdjustStockModal } from './AdjustStockModal'
import { MinimumStockModal } from './MinimumStockModal'
import { useInventory, useInventorySummary, type InventoryItem, type InventoryStatus } from './api'
import styles from './inventory.module.css'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

const STATUS_TONE: Record<InventoryStatus, 'danger' | 'warning' | 'success'> = { out: 'danger', low: 'warning', ok: 'success' }
const STATUS_LABEL: Record<InventoryStatus, string> = { out: 'OutOfStock', low: 'LowStock', ok: 'InStock' }

/** The status the server decided; an unknown value (a newer server) renders as plain "in stock" rather than crashing. */
export const statusOf = (value: string): InventoryStatus => (value === 'out' || value === 'low' ? value : 'ok')

/** `/app/inventory` - every product with its stock figures, the four tiles, adjust stock and set minimum. */
export default function InventoryPage() {
  const { t } = useTranslation(['inventory', 'common'])
  const canEdit = useCan(Permissions.Inventory_Edit)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useInventory(params)
  const summary = useInventorySummary()
  const [adjusting, setAdjusting] = useState<InventoryItem | null>(null)
  const [minimum, setMinimum] = useState<InventoryItem | null>(null)

  const columns: Column<InventoryItem>[] = [
    { key: 'name', header: t('Product'), sortable: true, required: true, cell: (p) => <strong className={styles.name}><bdi>{p.name}</bdi></strong> },
    { key: 'barcode', header: t('Barcode'), sortable: true, mono: true, hideBelow: 900, cell: (p) => <bdi>{p.barcode}</bdi> },
    {
      key: 'category',
      header: t('Category'),
      sortable: true,
      hideBelow: 1000,
      cell: (p) => (p.categoryName ? <bdi>{p.categoryName}</bdi> : <span className={styles.muted}>{t('NoCategory')}</span>),
    },
    {
      key: 'stock',
      header: t('CurrentStock'),
      sortable: true,
      align: 'center',
      cell: (p) => (
        <span className={styles.stock} data-status={statusOf(p.status)}>
          <bdi>{formatQty(p.currentStock)}</bdi>
          {p.unit && <span className={styles.unit}>{p.unit}</span>}
        </span>
      ),
    },
    { key: 'minimum', header: t('MinStock'), sortable: true, align: 'center', hideBelow: 1100, cell: (p) => formatQty(p.minimumStock ?? 0) },
    { key: 'maximum', header: t('MaxStock'), sortable: true, align: 'center', hideBelow: 1300, cell: (p) => formatQty(p.maximumStock ?? 0) },
    { key: 'cost', header: t('CostPrice'), sortable: true, align: 'end', hideBelow: 1200, cell: (p) => formatMoney(p.costPrice ?? 0, p.currencyNotation) },
    { key: 'price', header: t('SellingPrice'), sortable: true, align: 'end', hideBelow: 1000, cell: (p) => formatMoney(p.sellingPrice, p.currencyNotation) },
    { key: 'value', header: t('StockValue'), sortable: true, align: 'end', cell: (p) => formatMoney(p.stockValue, p.currencyNotation) },
    {
      key: 'status',
      header: t('Status'),
      sortable: true,
      align: 'center',
      cell: (p) => <StatusBadge tone={STATUS_TONE[statusOf(p.status)]} label={t(STATUS_LABEL[statusOf(p.status)])} />,
    },
  ]

  const stats = summary.data
  const count = (value: number | undefined) => (value === undefined ? '-' : formatNumber(value, 0))

  return (
    <div className={styles.page}>
      <PageHeader title={t('InventoryManagement')} count={list.data ? formatNumber(list.data.total, 0) : undefined}>
        <Link className={styles.link} to="/inventory/low-stock">
          {t('LowStockReport')}
        </Link>
        <Link className={styles.link} to="/inventory/stock-value">
          {t('StockValueReport')}
        </Link>
      </PageHeader>

      <div className={styles.tiles}>
        <StatTile label={t('TotalProducts')} value={count(stats?.totalProducts)} sub={t('AllProductsInInventory')} />
        <StatTile label={t('LowStock')} value={count(stats?.lowStockCount)} sub={t('BelowMinimumLevel')} />
        <StatTile label={t('OutOfStock')} value={count(stats?.outOfStockCount)} tone="danger" sub={t('ZeroQuantity')} />
        <StatTile label={t('TotalStockValue')} value={stats ? formatMoney(stats.totalStockValue, stats.currencyNotation) : '-'} tone="success" sub={t('AtCostPrice')} />
      </div>

      <DataTable<InventoryItem>
        caption={t('InventoryOverview')}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(p) => p.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.isError ? list.error.message : null}
        onRetry={() => void list.refetch()}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
        emptyTitle={params.q ? t('NoProductsMatch') : t('NoInventoryDataAvailable')}
        rowActionsLabel={t('Actions')}
        rowActions={
          canEdit
            ? (p) => (
                <>
                  <IconButton icon="plus" label={t('AdjustStock')} onClick={() => setAdjusting(p)} />
                  <IconButton icon="warning" label={t('SetMinimumStock')} onClick={() => setMinimum(p)} />
                </>
              )
            : undefined
        }
        toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchProducts')} aria-label={t('SearchProducts')} />}
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

      {adjusting && <AdjustStockModal product={adjusting} onClose={() => setAdjusting(null)} />}
      {minimum && <MinimumStockModal product={minimum} onClose={() => setMinimum(null)} />}
    </div>
  )
}
