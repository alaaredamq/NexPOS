import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { useSession } from '@/core/auth/useSession'
import { Can } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { StatusBadge } from '@/ui/StatusBadge'
import { confirm } from '@/ui/confirm'
import { formatDate, formatMoney, formatPercent, formatQty } from '@/ui/format'
import { toast } from '@/ui/toast'
import { flattenNav } from '@/shell/nav'
import { useDeleteProduct, useProduct, type ProductDetail } from './api'
import { useStickerLabel } from './labels/StickerLabelDialog'
import { formatStickerPrice } from './labels/stickerLabel'
import { NotFoundCard } from './NotFoundCard'
import styles from './ProductDetails.module.css'

/** `/products/:id`: everything about one product, with the actions the old details page had. */
export default function ProductDetailsPage() {
  const params = useParams()
  const id = Number(params.id)
  const validId = Number.isInteger(id) && id > 0
  const product = useProduct(validId ? id : null)

  if (!validId || product.error) return <NotFoundCard error={product.error} />
  if (product.isPending) return <Loader fullSection size="lg" />
  return <Details product={product.data} />
}

function Details({ product: p }: { product: ProductDetail }) {
  const { t } = useTranslation('products')
  const navigate = useNavigate()
  const session = useSession()
  const remove = useDeleteProduct()
  const label = useStickerLabel()
  const money = (amount: number) => formatMoney(amount, p.currencyNotation)
  const inventory = flattenNav(session.nav).find((item) => item.key === 'Inventory')

  const onDelete = async () => {
    const deleted = await confirm({
      title: t('DeleteProduct', { ns: 'common' }),
      message: t('AreYouSureYouWantToDeleteThisProduct'),
      detail: t('ThisActionCannotBeUndone', { ns: 'common' }),
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(p.id)
      },
    })
    if (!deleted) return
    toast.success(t('ProductDeletedSuccessfully', { ns: 'common' }))
    navigate('/products')
  }

  const stockTone = p.stockLevel === 'ok' ? 'success' : p.stockLevel === 'low' ? 'warning' : 'danger'
  const units = t('details.units', { count: formatQty(p.stockQuantity) })

  return (
    <div className={styles.page}>
      <div className={styles.top}>
        <div>
          <h1 className={styles.title}>{p.name}</h1>
          <p className={styles.subtitle}>{t('details.title')}</p>
        </div>
        <div className={styles.buttons}>
          {p.barcode && (
            <Button
              onClick={() => void label.open({ name: p.name, code: p.barcode, price: formatStickerPrice(p.sellingPrice, p.currencyNotation) })}
            >
              {t('details.printLabel')}
            </Button>
          )}
          <Can permission={Permissions.Products_Edit}>
            <Button variant="primary" onClick={() => navigate(`/products/${p.id}/edit`)}>
              {t('details.edit')}
            </Button>
          </Can>
          <Button onClick={() => navigate('/products')}>{t('Prod_BackToProducts', { ns: 'common' })}</Button>
        </div>
      </div>

      <div className={styles.layout}>
        <section className={styles.card} aria-label={t('details.information')}>
          <h2 className={styles.cardTitle}>{t('details.information')}</h2>
          <dl className={styles.grid}>
            <Item label={t('Product Name', { ns: 'common' })}>{p.name}</Item>
            <Item label={t('Barcode', { ns: 'common' })}>
              <span dir="ltr" className="mono">
                {p.barcode || t('details.na')}
              </span>
            </Item>
            <Item label={t('Description', { ns: 'common' })} wide>
              {p.description || t('details.noDescription')}
            </Item>
            <Item label={t('Category', { ns: 'common' })}>
              <StatusBadge tone="info" label={p.categoryName ?? t('details.uncategorized')} />
            </Item>
            <Item label={t('Supplier', { ns: 'common' })}>
              <StatusBadge tone="neutral" label={p.supplierName ?? t('details.noSupplier')} />
              {p.supplierDiscountRate > 0 && (
                <>
                  {' '}
                  <StatusBadge tone="warning" label={t('Prod_Discount', { 0: p.supplierDiscountRate, ns: 'common' })} />
                </>
              )}
            </Item>
            <Item label={t('Status')}>
              <StatusBadge tone={p.isActive ? 'success' : 'danger'} label={p.isActive ? t('Active') : t('Inactive')} />
            </Item>
            {p.isPack && (
              <Item label={t('Prod_IsPack', { ns: 'common' })} wide>
                {t('form.packReadOnly', { size: p.packSize ?? 0, name: p.singleProductName ?? '' })}
              </Item>
            )}
          </dl>

          <hr className={styles.rule} />
          <dl className={styles.grid3}>
            <Item label={t('Purchase Price', { ns: 'common' })}>
              <strong className="tabular-nums">{money(p.purchasePrice)}</strong>
            </Item>
            <Item label={t('Selling Price', { ns: 'common' })}>
              <strong className={`${styles.good} tabular-nums`}>{money(p.sellingPrice)}</strong>
            </Item>
            <Item label={t('details.profitMargin')}>
              <strong className="tabular-nums">{money(p.profitMargin)}</strong>
            </Item>
          </dl>

          {p.priceTiers.length > 0 && (
            <>
              <hr className={styles.rule} />
              <h3 className={styles.subTitle}>{t('Extra prices', { ns: 'common' })}</h3>
              <ul className={styles.tiers}>
                {p.priceTiers.map((tier) => (
                  <li key={tier.name ?? ""}>
                    <span>{tier.name ?? ""}</span>
                    <strong className="tabular-nums">{money(tier.price ?? 0)}</strong>
                  </li>
                ))}
              </ul>
            </>
          )}

          <hr className={styles.rule} />
          <dl className={styles.grid}>
            <Item label={t('Stock')}>
              <StatusBadge tone={stockTone} filled label={p.stockLevel === 'low' ? `${units} · ${t('LowStock')}` : p.stockLevel === 'out' ? `${units} · ${t('OutOfStock')}` : units} />
            </Item>
            <Item label={t('Minimum Stock Level', { ns: 'common' })}>{t('details.units', { count: formatQty(p.minStockLevel) })}</Item>
          </dl>
        </section>

        <div className={styles.side}>
          <section className={styles.card} aria-label={t('details.statistics')}>
            <h2 className={styles.cardTitle}>{t('details.statistics')}</h2>
            <dl className={styles.stats}>
              <Item label={t('details.stockValue')}>
                <strong className="tabular-nums">{money(p.stockValue)}</strong>
              </Item>
              <Item label={t('details.profitPercent')}>
                <strong className="tabular-nums">{formatPercent(p.profitMarginPercent, 2)}</strong>
              </Item>
              <Item label={t('Created', { ns: 'common' })}>{formatDate(p.createdAt)}</Item>
              {p.updatedAt && <Item label={t('details.lastUpdated')}>{formatDate(p.updatedAt)}</Item>}
            </dl>
          </section>

          <section className={styles.card} aria-label={t('details.actions')}>
            <h2 className={styles.cardTitle}>{t('details.actions')}</h2>
            <div className={styles.actions}>
              <Can permission={Permissions.Products_Edit}>
                <Button variant="primary" block onClick={() => navigate(`/products/${p.id}/edit`)}>
                  {t('details.edit')}
                </Button>
              </Can>
              <Can permission={Permissions.Products_Delete}>
                <Button variant="danger" block onClick={() => void onDelete()}>
                  {t('details.delete')}
                </Button>
              </Can>
              {inventory && (
                <a className={styles.link} href={`${inventory.href}?productId=${p.id}`}>
                  {t('details.viewInventory')}
                </a>
              )}
            </div>
          </section>
        </div>
      </div>
      {label.dialog}
    </div>
  )
}

function Item({ label, children, wide }: { label: string; children: React.ReactNode; wide?: boolean }) {
  return (
    <div className={styles.item} data-wide={wide || undefined}>
      <dt className={styles.itemLabel}>{label}</dt>
      <dd className={styles.itemValue}>{children}</dd>
    </div>
  )
}
