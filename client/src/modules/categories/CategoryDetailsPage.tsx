import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useSession } from '@/core/auth/useSession'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDateTime, formatMoney, formatNumber, formatQty } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { StatusBadge } from '@/ui/StatusBadge'
import { flattenNav } from '@/shell/nav'
import { NotFoundPage } from '@/shell/pages/pages'
import { useCategory, type CategoryProduct } from './api'
import { CategoryFormModal } from './CategoryFormModal'
import styles from './categories.module.css'

/** `/app/categories/:id` - facts, stock figures and the first 20 products of one category. */
export default function CategoryDetailsPage() {
  const { t } = useTranslation(['categories', 'common'])
  const id = Number(useParams().id)
  const canEdit = useCan(Permissions.Categories_Edit)
  const canSeeProducts = useCan(Permissions.Products_View)
  const productsHref = flattenNav(useSession().nav).find((item) => item.key === 'Products')?.href
  const query = useCategory(id)
  const [editing, setEditing] = useState(false)

  if (!Number.isInteger(id) || id < 1 || (isApiError(query.error) && query.error.status === 404)) return <NotFoundPage />
  if (query.isPending) return <Loader fullSection label={t('Loading', { ns: 'common' })} />
  if (query.isError) {
    return (
      <div className={styles.page}>
        <div className={styles.card} role="alert">
          {query.error.message}
          <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      </div>
    )
  }

  const { category, products, currencyNotation } = query.data
  const columns: Column<CategoryProduct>[] = [
    {
      key: 'name',
      header: t('ProductName', { ns: 'categories' }),
      required: true,
      cell: (p) => (
        <div>
          <strong><bdi>{p.name}</bdi></strong>
          {p.description && <div className={`${styles.muted} ${styles.clip}`}><bdi>{p.description}</bdi></div>}
        </div>
      ),
    },
    { key: 'barcode', header: t('Barcode', { ns: 'common' }), mono: true, hideBelow: 900, cell: (p) => <bdi>{p.barcode}</bdi> },
    { key: 'price', header: t('Price', { ns: 'common' }), align: 'end', cell: (p) => formatMoney(p.sellingPrice, p.currencyNotation) },
    {
      key: 'stock',
      header: t('Stock', { ns: 'common' }),
      align: 'center',
      cell: (p) => {
        const minimum = p.minimumStock ?? 0
        const tone = p.stockQuantity <= minimum ? 'danger' : p.stockQuantity <= minimum * 2 ? 'warning' : 'success'
        return <StatusBadge tone={tone} label={formatQty(p.stockQuantity)} />
      },
    },
    {
      key: 'status',
      header: t('Status', { ns: 'categories' }),
      align: 'center',
      hideBelow: 700,
      cell: (p) => <StatusBadge tone={p.isActive ? 'success' : 'neutral'} label={p.isActive ? t('Active', { ns: 'categories' }) : t('Inactive', { ns: 'categories' })} />,
    },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={category.name}
        back={{ to: '/categories', label: t('BackToCategories', { ns: 'categories' }) }}
        primaryActionLabel={canEdit ? t('EditCategory', { ns: 'categories' }) : undefined}
        onPrimaryAction={canEdit ? () => setEditing(true) : undefined}
      />

      <div className={styles.details}>
        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('CategoryInformation', { ns: 'categories' })}</h2>
            <dl className={styles.facts}>
              <div>
                <dt>{t('CategoryName', { ns: 'categories' })}</dt>
                <dd><bdi>{category.name}</bdi></dd>
              </div>
              <div>
                <dt>{t('Status', { ns: 'categories' })}</dt>
                <dd><StatusBadge tone={category.isActive ? 'success' : 'neutral'} label={category.isActive ? t('Active', { ns: 'categories' }) : t('Inactive', { ns: 'categories' })} /></dd>
              </div>
              <div className={styles.factWide}>
                <dt>{t('Description', { ns: 'categories' })}</dt>
                <dd>{category.description ? <bdi>{category.description}</bdi> : <span className={styles.muted}>{t('NoDescription', { ns: 'categories' })}</span>}</dd>
              </div>
              <div>
                <dt>{t('CreatedOn', { ns: 'categories' })}</dt>
                <dd>{formatDateTime(category.createdAt)}</dd>
              </div>
              <div>
                <dt>{t('Products', { ns: 'categories' })}</dt>
                <dd>{formatNumber(category.productCount, 0)}</dd>
              </div>
            </dl>
          </section>

          <DataTable<CategoryProduct>
            caption={t('ProductsInCategory', { ns: 'categories' })}
            columns={columns}
            rows={products}
            rowKey={(p) => p.id}
            emptyTitle={t('NoProductsInCategory', { ns: 'categories' })}
            footer={
              category.productCount > products.length && canSeeProducts && productsHref ? (
                <div className={styles.card}>
                  <a className={`${styles.link} ${styles.viewAll}`} href={`${productsHref}?categoryId=${category.id}`}>
                    {t('ViewAllProducts', { ns: 'categories', 0: formatNumber(category.productCount, 0) })}
                  </a>
                </div>
              ) : undefined
            }
          />
        </div>

        <section className={styles.card}>
          <h2 className={styles.cardTitle}>{t('CategorySummary', { ns: 'categories' })}</h2>
          <dl className={styles.summary}>
            <div className={styles.summaryRow}>
              <dt>{t('TotalProducts', { ns: 'categories' })}</dt>
              <dd>{formatNumber(category.productCount, 0)}</dd>
            </div>
            <div className={styles.summaryRow}>
              <dt>{t('ActiveProducts', { ns: 'categories' })}</dt>
              <dd>{formatNumber(query.data.activeProducts, 0)}</dd>
            </div>
            <div className={styles.summaryRow}>
              <dt>{t('TotalStock', { ns: 'categories' })}</dt>
              <dd>{formatQty(query.data.totalStock)}</dd>
            </div>
            <div className={styles.summaryRow}>
              <dt>{t('TotalValue', { ns: 'categories' })}</dt>
              <dd>{formatMoney(query.data.totalValue, currencyNotation)}</dd>
            </div>
          </dl>
        </section>
      </div>

      {editing && <CategoryFormModal category={category} onClose={() => setEditing(false)} />}
    </div>
  )
}
