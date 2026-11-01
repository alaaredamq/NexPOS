import { useMemo, useState } from 'react'
import { FormProvider, useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { useHref, useNavigate, useParams } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { formatMoney, ltrIsolate } from '@/ui/format'
import { FormError } from '@/ui/form/FormError'
import { MoneyField } from '@/ui/form/MoneyField'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { toast } from '@/ui/toast'
import { productsApi, useProduct, useProductLookups, useSaveProduct, type ProductDetail, type ProductLookups } from './api'
import {
  UNITS,
  emptyProductForm,
  formFromProduct,
  productFormSchema,
  toSaveRequest,
  zodResolver,
  type ProductFormMessages,
  type ProductFormValues,
} from './form'
import { useStickerLabel } from './labels/StickerLabelDialog'
import { formatStickerPrice } from './labels/stickerLabel'
import { NotFoundCard } from './NotFoundCard'
import { PriceTiersEditor } from './PriceTiersEditor'
import styles from './ProductForm.module.css'

/** `/products/new` and `/products/:id/edit`: the same form, loaded with the lookups (and the product when editing). */
export default function ProductFormPage() {
  const params = useParams()
  const id = params.id === undefined ? null : Number(params.id)
  const validId = id === null || (Number.isInteger(id) && id > 0)
  const lookups = useProductLookups()
  const product = useProduct(validId ? id : null)

  if (!validId || product.error) return <NotFoundCard error={product.error} />
  if (lookups.isPending || (id !== null && product.isPending)) return <Loader fullSection size="lg" />
  if (lookups.error) return <NotFoundCard error={lookups.error} />

  return <ProductForm key={id ?? 'new'} lookups={lookups.data} product={product.data} />
}

function ProductForm({ lookups, product }: { lookups: ProductLookups; product: ProductDetail | undefined }) {
  const { t } = useTranslation('products')
  const navigate = useNavigate()
  const editing = product !== undefined
  const save = useSaveProduct(product?.id ?? null)
  const label = useStickerLabel()
  const newProductHref = useHref('/products/new')
  const [formErrors, setFormErrors] = useState<string[]>([])
  const [generating, setGenerating] = useState(false)

  const messages = useMemo<ProductFormMessages>(
    () => ({
      nameRequired: t('Prod_NameRequired', { ns: 'common' }),
      nameLength: t('Prod_NameLength', { ns: 'common' }),
      barcodeRequired: t('Barcode is required', { ns: 'common' }),
      categoryRequired: t('Category is required', { ns: 'common' }),
      supplierRequired: t('Supplier is required', { ns: 'common' }),
      purchasePrice: t('Purchase price must be greater than 0', { ns: 'common' }),
      sellingPrice: t('Selling price must be greater than 0', { ns: 'common' }),
      sellingAboveCost: t('Prod_PriceWarn', { ns: 'common' }),
      packSize: t('Pack size must be greater than 0', { ns: 'common' }),
      singleProduct: t('Prod_PackSingleRequired', { ns: 'common' }),
      tierName: t('Price name required', { ns: 'common' }),
      tierAmount: t('Price amount required', { ns: 'common' }),
      tierBelowCost: t('Price below purchase', { ns: 'common' }),
    }),
    [t],
  )

  const form = useForm<ProductFormValues>({
    resolver: zodResolver(productFormSchema(messages, editing ? 'edit' : 'create')),
    defaultValues: editing ? formFromProduct(product) : emptyProductForm(lookups),
  })
  const { control, handleSubmit, setValue, getValues, clearErrors } = form
  const [currencyId, purchase, selling, isPack] = useWatch({ control, name: ['currencyId', 'purchasePrice', 'sellingPrice', 'isPack'] })
  const notation = lookups.currencies.find((c) => c.id === currencyId)?.notation ?? ''
  const profit = (selling ?? 0) - (purchase ?? 0)

  const categoryOptions = useMemo(() => lookups.categories.map((c) => ({ value: c.id, label: c.name })), [lookups.categories])
  const supplierOptions = useMemo(
    () =>
      lookups.suppliers.map((s) => ({
        value: s.id,
        label: s.discountRate > 0 ? `${s.name} (${t('Prod_Discount', { 0: s.discountRate, ns: 'common' })})` : s.name,
      })),
    [lookups.suppliers, t],
  )
  const currencyOptions = useMemo(() => lookups.currencies.map((c) => ({ value: c.id, label: ltrIsolate(`${c.code} (${c.notation})`) })), [lookups.currencies])
  const unitOptions = useMemo(() => UNITS.map((u) => ({ value: u, label: t(`Prod_Unit_${u}`, { ns: 'common' }) })), [t])

  const onSubmit = handleSubmit(async (values) => {
    setFormErrors([])
    try {
      const saved = await save.mutateAsync(toSaveRequest(values))
      toast.success(editing ? t('form.updated') : t('form.created'))
      navigate(editing ? `/products/${saved.id}` : '/products')
    } catch (error) {
      if (!isApiError(error)) throw error
      const unmatched = applyServerErrors(form, { errors: error.errors, detail: error.message, code: error.code })
      setFormErrors(unmatched.length > 0 || error.errors ? unmatched : [error.message])
    }
  })

  const generateBarcode = async () => {
    setGenerating(true)
    try {
      const { barcode } = await productsApi.generateBarcode()
      setValue('barcode', barcode, { shouldDirty: true })
      clearErrors('barcode')
    } catch {
      toast.error(t('Prod_GenFail', { ns: 'common' }))
    } finally {
      setGenerating(false)
    }
  }

  // Works before saving: the label is made from what is in the form right now.
  const printLabel = () =>
    void label.open({
      name: getValues('name'),
      code: getValues('barcode'),
      price: formatStickerPrice(getValues('sellingPrice') ?? 0, notation),
    })

  const loadSingleProducts = async (query: string) => {
    if (query.trim().length < 3) return []
    return (await productsApi.options(query.trim())).map((o) => ({ value: o.id, label: o.name, hint: o.barcode }))
  }

  return (
    <div className={styles.page}>
      <div className={styles.top}>
        <div>
          <h1 className={styles.title}>{editing ? t('form.editTitle') : t('Prod_CreateTitle', { ns: 'common' })}</h1>
          <p className={styles.subtitle}>{editing ? product.name : t('Prod_CreateSubtitle', { ns: 'common' })}</p>
        </div>
        <Button onClick={() => navigate('/products')}>{t('Prod_BackToProducts', { ns: 'common' })}</Button>
      </div>

      <FormProvider {...form}>
        <form className={styles.layout} onSubmit={onSubmit} noValidate>
          <div className={styles.card}>
            <h2 className={styles.cardTitle}>{t('Prod_Information', { ns: 'common' })}</h2>
            <FormError messages={formErrors} />

            <div className={styles.row}>
              <TextField<ProductFormValues> name="name" label={t('Product Name', { ns: 'common' })} placeholder={t('Prod_PhName', { ns: 'common' })} required maxLength={255} autoFocus />
              <div>
                <TextField<ProductFormValues> name="barcode" label={t('Barcode', { ns: 'common' })} placeholder={t('Prod_PhBarcode', { ns: 'common' })} required maxLength={50} dir="ltr" />
                <div className={styles.barcodeTools}>
                  <Button loading={generating} onClick={() => void generateBarcode()} title={t('Prod_GenerateTip', { ns: 'common' })}>
                    {t('Prod_Generate', { ns: 'common' })}
                  </Button>
                  <Button onClick={printLabel} title={t('Prod_PrintTip', { ns: 'common' })}>
                    {t('Prod_Print', { ns: 'common' })}
                  </Button>
                </div>
              </div>
            </div>

            <TextField<ProductFormValues> name="description" label={t('Description', { ns: 'common' })} placeholder={t('Prod_PhDescription', { ns: 'common' })} multiline rows={3} maxLength={1000} />

            <div className={styles.row}>
              <SelectField<ProductFormValues, number> name="categoryId" label={t('Category', { ns: 'common' })} options={categoryOptions} placeholder={t('Prod_PhCategory', { ns: 'common' })} required />
              <SelectField<ProductFormValues, number> name="supplierId" label={t('Supplier', { ns: 'common' })} options={supplierOptions} placeholder={t('Prod_PhSupplier', { ns: 'common' })} required />
            </div>

            <div className={styles.row4}>
              <MoneyField<ProductFormValues> name="purchasePrice" label={t('Purchase Price', { ns: 'common' })} currency={notation} placeholder="0.00" required />
              <MoneyField<ProductFormValues> name="sellingPrice" label={t('Selling Price', { ns: 'common' })} currency={notation} placeholder="0.00" required />
              <SelectField<ProductFormValues, number> name="currencyId" label={t('Currency', { ns: 'common' })} options={currencyOptions} placeholder={t('Prod_PhCurrency', { ns: 'common' })} />
              <SelectField<ProductFormValues, string> name="unit" label={t('Unit', { ns: 'common' })} options={unitOptions} placeholder={t('Prod_PhUnit', { ns: 'common' })} clearable />
            </div>

            <PriceTiersEditor currency={notation} />

            <div className={styles.row}>
              <NumberField<ProductFormValues> name="stockQuantity" label={editing ? t('Stock', { ns: 'common' }) : t('Initial Stock Quantity', { ns: 'common' })} min={0} placeholder="0" />
              <NumberField<ProductFormValues> name="minStockLevel" label={t('Minimum Stock Level', { ns: 'common' })} min={0} placeholder="0" />
            </div>

            <SwitchField<ProductFormValues> name="isActive" label={t('Prod_IsActive', { ns: 'common' })} />

            {editing ? (
              product.isPack && (
                <p className={styles.muted}>{t('form.packReadOnly', { size: product.packSize ?? 0, name: product.singleProductName ?? '' })}</p>
              )
            ) : (
              <>
                <SwitchField<ProductFormValues> name="isPack" label={t('Prod_IsPack', { ns: 'common' })} />
                {isPack && (
                  <div className={styles.packBox}>
                    <div className={styles.row}>
                      <NumberField<ProductFormValues> name="packSize" label={t('Pack Size', { ns: 'common' })} decimals={0} min={1} placeholder={t('Prod_PhPackSize', { ns: 'common' })} hint={t('Prod_PackSizeHelp', { ns: 'common' })} required />
                      <SelectField<ProductFormValues, number>
                        name="singleProductId"
                        label={t('Single Product', { ns: 'common' })}
                        loadOptions={loadSingleProducts}
                        placeholder={t('Prod_SearchPlaceholder', { ns: 'common' })}
                        hint={t('Prod_SingleProductHelp', { ns: 'common' })}
                        required
                        clearable
                      />
                    </div>
                    {/* The old page opened product creation in a pop-up window so the single product could be added first. */}
                    <div>
                      <a className={styles.link} href={newProductHref} target="_blank" rel="noopener">
                        {t('Prod_AddProduct', { ns: 'common' })}
                      </a>
                      <p className={styles.muted}>{t('Prod_AddProductHelp', { ns: 'common' })}</p>
                    </div>
                  </div>
                )}
              </>
            )}

            <div className={styles.actions}>
              <Button onClick={() => navigate(editing ? `/products/${product.id}` : '/products')}>{t('Cancel', { ns: 'common' })}</Button>
              <Button type="submit" variant="primary" loading={save.isPending}>
                {editing ? t('form.save') : t('Prod_CreateButton', { ns: 'common' })}
              </Button>
            </div>
          </div>

          <aside className={styles.card} aria-label={t('Prod_Guidelines', { ns: 'common' })}>
            <h2 className={styles.cardTitle}>{t('Prod_Guidelines', { ns: 'common' })}</h2>
            <ul className={styles.guidelines}>
              <li>{t('Prod_G1', { ns: 'common' })}</li>
              <li>{t('Prod_G2', { ns: 'common' })}</li>
              <li>{t('Prod_G3', { ns: 'common' })}</li>
              <li>{t('Prod_G4', { ns: 'common' })}</li>
              <li>{t('Prod_G5', { ns: 'common' })}</li>
            </ul>
            <hr className={styles.rule} />
            <h2 className={styles.cardTitle}>{t('Prod_ProfitPreview', { ns: 'common' })}</h2>
            <div className={styles.profit}>
              <div className={styles.profitRow}>
                <span>{t('Prod_PurchasePriceL', { ns: 'common' })}</span>
                <span className="tabular-nums">{formatMoney(purchase ?? 0, notation)}</span>
              </div>
              <div className={styles.profitRow}>
                <span>{t('Prod_SellingPriceL', { ns: 'common' })}</span>
                <span className="tabular-nums">{formatMoney(selling ?? 0, notation)}</span>
              </div>
              <div className={`${styles.profitRow} ${styles.profitTotal}`}>
                <span>{t('Prod_ProfitMargin', { ns: 'common' })}</span>
                <span className="tabular-nums" data-sign={profit > 0 ? 'positive' : profit < 0 ? 'negative' : undefined}>
                  {formatMoney(profit, notation)}
                </span>
              </div>
            </div>
          </aside>
        </form>
      </FormProvider>
      {label.dialog}
    </div>
  )
}
