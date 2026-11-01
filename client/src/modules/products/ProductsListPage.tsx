import { useMemo, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { Can, useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { DataTable, type Column, type SortState } from '@/ui/DataTable'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { Select } from '@/ui/Select'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { confirm } from '@/ui/confirm'
import { formatMoney, formatNumber, formatQty } from '@/ui/format'
import { toast } from '@/ui/toast'
import {
  productsApi,
  saveDownload,
  useDeleteProduct,
  useProductList,
  useProductLookups,
  useProductSummary,
  type ProductListItem,
  type ProductListParams,
  type StatusFilter,
  type StockFilter,
} from './api'
import { ImportDialog } from './ImportDialog'
import { useStickerLabel } from './labels/StickerLabelDialog'
import { formatStickerPrice } from './labels/stickerLabel'
import { PAGE_SIZES, readListParams, writeListParams } from './listParams'
import styles from './ProductsList.module.css'

/** `/products`: summary tiles, filters mirrored in the URL, a server-paged table with row actions, CSV import/export. */
export default function ProductsListPage() {
  const { t } = useTranslation('products')
  const navigate = useNavigate()
  const [search, setSearch] = useSearchParams()
  const params = useMemo(() => readListParams(search), [search])
  const list = useProductList(params)
  const summary = useProductSummary()
  const lookups = useProductLookups()
  const remove = useDeleteProduct()
  const label = useStickerLabel()
  const canEdit = useCan(Permissions.Products_Edit)
  const canDelete = useCan(Permissions.Products_Delete)
  const canCreate = useCan(Permissions.Products_Create)
  const [importing, setImporting] = useState(false)
  const [exporting, setExporting] = useState(false)

  // Any filter change goes back to page 1; replace keeps typing in the search box out of the history.
  const update = (patch: Partial<ProductListParams>, resetPage = true) =>
    setSearch(writeListParams({ ...params, ...patch, page: resetPage ? 1 : (patch.page ?? params.page) }), { replace: true })

  const sort: SortState = { key: params.sort, dir: params.dir }
  const categoryOptions = useMemo(() => (lookups.data?.categories ?? []).map((c) => ({ value: c.id, label: c.name })), [lookups.data])
  const category = categoryOptions.find((o) => o.value === params.categoryId) ?? null

  const onDelete = (p: ProductListItem) =>
    void confirm({
      title: t('DeleteProduct', { ns: 'common' }),
      message: t('AreYouSureYouWantToDeleteThisProduct'),
      detail: t('ThisActionCannotBeUndone', { ns: 'common' }),
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(p.id)
        toast.success(t('ProductDeletedSuccessfully', { ns: 'common' }))
      },
    })

  const onExport = async () => {
    setExporting(true)
    try {
      saveDownload(await productsApi.export(), 'products.csv')
    } catch (e) {
      toast.error(isApiError(e) ? e.message : t('list.exportFailed'))
    } finally {
      setExporting(false)
    }
  }

  const columns: Column<ProductListItem>[] = [
    {
      key: 'name',
      header: t('Name'),
      sortable: true,
      required: true,
      cell: (p) => <strong>{p.name}</strong>,
    },
    { key: 'barcode', header: t('Barcode'), sortable: true, mono: true, hideBelow: 700, cell: (p) => <span dir="ltr">{p.barcode}</span> },
    { key: 'category', header: t('Category'), sortable: true, hideBelow: 900, cell: (p) => p.categoryName ?? t('list.noCategory') },
    {
      key: 'purchasePrice',
      header: t('PurchasePrice'),
      sortable: true,
      align: 'end',
      hideBelow: 1100,
      cell: (p) => <span className="tabular-nums">{formatMoney(p.purchasePrice, p.currencyNotation)}</span>,
    },
    {
      key: 'sellingPrice',
      header: t('SellingPrice'),
      sortable: true,
      align: 'end',
      cell: (p) => <span className="tabular-nums">{formatMoney(p.sellingPrice, p.currencyNotation)}</span>,
    },
    {
      key: 'stock',
      header: t('Stock'),
      sortable: true,
      align: 'center',
      // The level (out / low / ok) is decided by the server, the same rule as the tiles above.
      cell: (p) => (
        <strong className={`${styles.stock} tabular-nums`} data-level={p.stockLevel}>
          {formatQty(p.stockQuantity)}
        </strong>
      ),
    },
    {
      key: 'status',
      header: t('Status'),
      sortable: true,
      align: 'center',
      cell: (p) => <StatusBadge tone={p.isActive ? 'success' : 'neutral'} label={p.isActive ? t('Active') : t('Inactive')} />,
    },
  ]

  const statusOptions: { value: StatusFilter; label: string }[] = [
    { value: 'all', label: t('All', { ns: 'common' }) },
    { value: 'active', label: t('Active') },
    { value: 'inactive', label: t('Inactive') },
  ]
  const stockOptions: { value: StockFilter; label: string }[] = [
    { value: 'all', label: t('All', { ns: 'common' }) },
    { value: 'low', label: t('LowStock') },
    { value: 'out', label: t('OutOfStock') },
  ]

  const filtered = params.q !== '' || params.categoryId !== null || params.status !== 'all' || params.stock !== 'all'
  const s = summary.data

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('Products')}
        count={s ? formatNumber(s.total, 0) : undefined}
        primaryActionLabel={t('AddProduct')}
        onPrimaryAction={canCreate ? () => navigate('/products/new') : undefined}
      >
        <Can permission={[Permissions.Products_Create, Permissions.Products_Edit]}>
          <Button onClick={() => setImporting(true)}>{t('Prod_Import')}</Button>
        </Can>
        <Button loading={exporting} onClick={() => void onExport()}>
          {t('Prod_Export')}
        </Button>
      </PageHeader>

      <div className={styles.tiles}>
        <StatTile label={t('TotalProducts')} value={s ? formatNumber(s.total, 0) : '–'} />
        <StatTile label={t('ActiveProducts')} value={s ? formatNumber(s.active, 0) : '–'} tone="success" />
        <StatTile
          label={t('LowStock')}
          value={s ? formatNumber(s.lowStock, 0) : '–'}
          sub={s ? t('list.lowStockSub', { 0: formatQty(s.lowStockThreshold) }) : undefined}
          tone={s && s.lowStock > 0 ? 'danger' : 'default'}
        />
        <StatTile label={t('OutOfStock')} value={s ? formatNumber(s.outOfStock, 0) : '–'} tone={s && s.outOfStock > 0 ? 'danger' : 'default'} />
      </div>

      <DataTable
        caption={t('ProductsList')}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(p) => p.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.error ? (isApiError(list.error) ? list.error.message : t('list.loadFailed')) : null}
        onRetry={() => void list.refetch()}
        emptyTitle={filtered ? t('list.emptyFiltered') : t('list.empty')}
        emptyAction={
          !filtered && (
            <Can permission={Permissions.Products_Create}>
              <Button variant="primary" onClick={() => navigate('/products/new')}>
                {t('AddProduct')}
              </Button>
            </Can>
          )
        }
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: 'name', dir: 'asc' }, false)}
        columnChooserKey="nexpos.products.columns"
        onRowClick={(p) => navigate(`/products/${p.id}`)}
        toolbar={
          <div className={styles.filters}>
            <div className={styles.search}>
              <SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('list.searchPlaceholder')} aria-label={t('list.searchPlaceholder')} />
            </div>
            <div className={styles.category}>
              <Select
                value={category}
                onChange={(o) => update({ categoryId: o ? Number(o.value) : null })}
                options={categoryOptions}
                placeholder={t('list.allCategories')}
                aria-label={t('Category')}
                clearable
              />
            </div>
            <div className={styles.chips}>
              <span className={styles.chipsLabel}>{t('Status')}</span>
              <ChipGroup size="sm" aria-label={t('Status')} options={statusOptions} value={params.status} onChange={(v) => update({ status: v as StatusFilter })} />
            </div>
            <div className={styles.chips}>
              <span className={styles.chipsLabel}>{t('Stock')}</span>
              <ChipGroup size="sm" aria-label={t('Stock')} options={stockOptions} value={params.stock} onChange={(v) => update({ stock: v as StockFilter })} />
            </div>
          </div>
        }
        rowActions={(p) => (
          <>
            {canEdit && <IconButton icon="edit" label={t('details.edit')} onClick={() => navigate(`/products/${p.id}/edit`)} />}
            <IconButton
              icon="print"
              label={t('list.printLabel')}
              onClick={() => void label.open({ name: p.name, code: p.barcode, price: formatStickerPrice(p.sellingPrice, p.currencyNotation) })}
            />
            {canDelete && <IconButton icon="trash" tone="danger" label={t('details.delete')} onClick={() => onDelete(p)} />}
          </>
        )}
        footer={
          <Pagination
            page={params.page}
            pageSize={params.pageSize}
            total={list.data?.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={(page) => update({ page }, false)}
            onPageSizeChange={(pageSize) => update({ pageSize })}
          />
        }
      />

      {importing && <ImportDialog onClose={() => setImporting(false)} />}
      {label.dialog}
    </div>
  )
}
