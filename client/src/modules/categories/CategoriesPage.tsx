import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { downloadCategoriesCsv, useCategories, useCategorySummary, useDeleteCategory, type Category } from './api'
import { CategoryFormModal } from './CategoryFormModal'
import { PAGE_SIZES, useListParams } from './useListParams'
import styles from './categories.module.css'

const DEFAULT_SORT = { key: 'name', dir: 'asc' } as const

/** `/app/categories` - the list, the four tiles, create / edit in a dialog, delete, CSV export. */
export default function CategoriesPage() {
  const { t } = useTranslation(['categories', 'common'])
  const navigate = useNavigate()
  const canCreate = useCan(Permissions.Categories_Create)
  const canEdit = useCan(Permissions.Categories_Edit)
  const canDelete = useCan(Permissions.Categories_Delete)

  const { params, sort, update } = useListParams(DEFAULT_SORT)
  const list = useCategories(params)
  const summary = useCategorySummary()
  const remove = useDeleteCategory()
  const [editing, setEditing] = useState<Category | 'new' | null>(null)
  const [exporting, setExporting] = useState(false)

  const askDelete = async (category: Category) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('AreYouSureYouWantToDeleteThisCategory', { ns: 'categories' }),
      detail: category.name,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        try {
          await remove.mutateAsync(category.id)
        } catch (error) {
          // `category_in_use` is the one refusal users meet: say it in the page's own words.
          if (isApiError(error) && error.code === 'category_in_use') throw new Error(t('CategoryInUse', { ns: 'categories' }))
          throw error
        }
        toast.success(t('CategoryDeleted', { ns: 'categories' }))
      },
    })
  }

  const exportCsv = async () => {
    setExporting(true)
    try {
      await downloadCategoriesCsv()
    } catch (error) {
      toast.error(error instanceof Error ? error.message : t('common.actionFailed', { ns: 'common' }))
    } finally {
      setExporting(false)
    }
  }

  const columns: Column<Category>[] = [
    {
      key: 'name',
      header: t('Name', { ns: 'categories' }),
      sortable: true,
      required: true,
      cell: (c) => (
        <Link className={styles.name} to={`/categories/${c.id}`}>
          <bdi>{c.name}</bdi>
        </Link>
      ),
    },
    {
      key: 'description',
      header: t('Description', { ns: 'categories' }),
      sortable: true,
      hideBelow: 900,
      cell: (c) =>
        c.description ? (
          <bdi className={styles.clip} title={c.description}>
            {c.description}
          </bdi>
        ) : (
          <span className={styles.muted}>{t('NoDescription', { ns: 'categories' })}</span>
        ),
    },
    { key: 'productCount', header: t('Products', { ns: 'categories' }), sortable: true, align: 'center', cell: (c) => formatNumber(c.productCount, 0) },
    {
      key: 'isActive',
      header: t('Status', { ns: 'categories' }),
      sortable: true,
      align: 'center',
      cell: (c) => <StatusBadge tone={c.isActive ? 'success' : 'neutral'} label={c.isActive ? t('Active', { ns: 'categories' }) : t('Inactive', { ns: 'categories' })} />,
    },
  ]

  const stats = summary.data
  const tile = (value: number | undefined) => (value === undefined ? '-' : formatNumber(value, 0))

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('Categories', { ns: 'categories' })}
        count={list.data ? formatNumber(list.data.total, 0) : undefined}
        primaryActionLabel={canCreate ? t('AddCategory', { ns: 'categories' }) : undefined}
        onPrimaryAction={canCreate ? () => setEditing('new') : undefined}
      >
        <Button onClick={() => void exportCsv()} loading={exporting}>
          {t('ExportCategories', { ns: 'categories' })}
        </Button>
      </PageHeader>

      <div className={styles.tiles}>
        <StatTile label={t('TotalCategories', { ns: 'categories' })} value={tile(stats?.total)} />
        <StatTile label={t('ActiveCategories', { ns: 'categories' })} value={tile(stats?.active)} tone="success" />
        <StatTile label={t('InactiveCategories', { ns: 'categories' })} value={tile(stats?.inactive)} />
        <StatTile label={t('TotalProducts', { ns: 'categories' })} value={tile(stats?.products)} sub={t('InAllCategories', { ns: 'categories' })} />
      </div>

      <DataTable<Category>
        caption={t('CategoriesList', { ns: 'categories' })}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(c) => c.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.isError ? list.error.message : null}
        onRetry={() => void list.refetch()}
        sort={sort}
        onSortChange={(next) => update(next ? { sort: next.key, dir: next.dir } : { sort: DEFAULT_SORT.key, dir: DEFAULT_SORT.dir })}
        onRowClick={(c) => void navigate(`/categories/${c.id}`)}
        emptyTitle={params.q ? t('NoCategoriesMatch', { ns: 'categories' }) : t('NoCategories', { ns: 'categories' })}
        emptyMessage={params.q ? undefined : t('NoCategoriesMessage', { ns: 'categories' })}
        rowActionsLabel={t('Actions', { ns: 'categories' })}
        rowActions={
          canEdit || canDelete
            ? (c) => (
                <>
                  {canEdit && <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(c)} />}
                  {canDelete && <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(c)} />}
                </>
              )
            : undefined
        }
        toolbar={<SearchBox value={params.q} onSearch={(q) => update({ q })} placeholder={t('SearchCategories', { ns: 'categories' })} aria-label={t('SearchCategories', { ns: 'categories' })} />}
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

      {editing && <CategoryFormModal category={editing === 'new' ? undefined : editing} onClose={() => setEditing(null)} />}
    </div>
  )
}
