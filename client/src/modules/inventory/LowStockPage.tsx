import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatNumber, formatQty } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { PAGE_SIZES, useListParams } from '../_shared/useListParams'
import { AdjustStockModal } from './AdjustStockModal'
import { MinimumStockModal } from './MinimumStockModal'
import { PrintButtons, PrintFailure, useReportPrinting } from './PrintButtons'
import { useLowStockReport, type LowStockItem, type LowStockPriority } from './api'
import styles from './inventory.module.css'

// The server sorts this report (least stock first); the page only searches and pages.
const NO_SORT = { key: 'stock', dir: 'asc' } as const

const PRIORITY_TONE: Record<LowStockPriority, 'danger' | 'warning' | 'info'> = { critical: 'danger', high: 'warning', medium: 'info' }
const PRIORITY_LABEL: Record<LowStockPriority, string> = { critical: 'Critical', high: 'High', medium: 'Medium' }
const priorityOf = (value: string): LowStockPriority => (value === 'critical' || value === 'high' ? value : 'medium')

/** `/app/inventory/low-stock` - products at or below their minimum, how far under, and how urgent. */
export default function LowStockPage() {
  const { t } = useTranslation(['inventory', 'common'])
  const canEdit = useCan(Permissions.Inventory_Edit)

  const { params, update } = useListParams(NO_SORT)
  const report = useLowStockReport(params)
  const printing = useReportPrinting('low-stock')
  const [adjusting, setAdjusting] = useState<LowStockItem | null>(null)
  const [minimum, setMinimum] = useState<LowStockItem | null>(null)

  const columns: Column<LowStockItem>[] = [
    {
      key: 'name',
      header: t('Product'),
      required: true,
      cell: (p) => (
        <div>
          <strong className={styles.name}><bdi>{p.name}</bdi></strong>
          {p.description && <div className={`${styles.muted} ${styles.clip}`}><bdi>{p.description}</bdi></div>}
        </div>
      ),
    },
    { key: 'barcode', header: t('Barcode'), mono: true, hideBelow: 900, cell: (p) => <bdi>{p.barcode}</bdi> },
    {
      key: 'category',
      header: t('Category'),
      hideBelow: 1000,
      cell: (p) => (p.categoryName ? <bdi>{p.categoryName}</bdi> : <span className={styles.muted}>{t('NoCategory')}</span>),
    },
    {
      key: 'stock',
      header: t('CurrentStock'),
      align: 'center',
      cell: (p) => (
        <span className={styles.stock} data-status={p.currentStock <= 0 ? 'out' : 'low'}>
          <bdi>{formatQty(p.currentStock)}</bdi>
          {p.unit && <span className={styles.unit}>{p.unit}</span>}
        </span>
      ),
    },
    { key: 'minimum', header: t('MinimumStock'), align: 'center', cell: (p) => formatQty(p.minimumStock) },
    { key: 'deficit', header: t('StockDeficit'), align: 'center', cell: (p) => <span className={styles.deficit}>{formatQty(p.deficit)}</span> },
    {
      key: 'priority',
      header: t('Priority'),
      align: 'center',
      cell: (p) => <StatusBadge tone={PRIORITY_TONE[priorityOf(p.priority)]} label={t(PRIORITY_LABEL[priorityOf(p.priority)])} />,
    },
  ]

  const summary = report.data?.summary
  const count = (value: number | undefined) => (value === undefined ? '-' : formatNumber(value, 0))

  return (
    <div className={styles.page}>
      <PageHeader title={t('LowStockReport')} back={{ to: '/inventory', label: t('BackToInventory') }} count={report.data ? formatNumber(report.data.items.total, 0) : undefined}>
        <PrintButtons printing={printing} />
      </PageHeader>
      <PrintFailure printing={printing} />

      <div className={`${styles.tiles} ${styles.tilesThree}`}>
        <StatTile label={t('ProductsWithLowStock')} value={count(summary?.products)} tone="info" />
        <StatTile label={t('OutOfStock')} value={count(summary?.outOfStock)} tone="danger" />
        <StatTile label={t('TotalUnitsAvailable')} value={summary ? formatQty(summary.totalUnits) : '-'} />
      </div>

      <DataTable<LowStockItem>
        caption={t('LowStockProductsDetails')}
        columns={columns}
        rows={report.data?.items.items ?? []}
        rowKey={(p) => p.id}
        loading={report.isPending}
        refreshing={report.isFetching && !report.isPending}
        error={report.isError ? report.error.message : null}
        onRetry={() => void report.refetch()}
        emptyTitle={params.q ? t('NoProductsMatch') : t('GreatNews')}
        emptyMessage={params.q ? undefined : t('AllProductsSufficientStock')}
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
            total={report.data?.items.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={(page) => update({ page })}
            onPageSizeChange={(pageSize) => update({ pageSize })}
          />
        }
      />

      {adjusting && <AdjustStockModal product={{ id: adjusting.id, name: adjusting.name, currentStock: adjusting.currentStock, unit: adjusting.unit }} onClose={() => setAdjusting(null)} />}
      {minimum && <MinimumStockModal product={{ id: minimum.id, name: minimum.name, minimumStock: minimum.minimumStock, unit: minimum.unit }} onClose={() => setMinimum(null)} />}
    </div>
  )
}
