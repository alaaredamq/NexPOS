import { useCallback, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useHref, useNavigate, useSearchParams } from 'react-router-dom'
import { http, isApiError } from '@/api/http'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { Field } from '@/ui/form/Field'
import field from '@/ui/form/field.module.css'
import { FormError } from '@/ui/form/FormError'
import { NumericInput } from '@/ui/form/NumericInput'
import { formatMoney, formatNumber, toDateInputValue } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { PageHeader } from '@/ui/PageHeader'
import { Select, type SelectOption } from '@/ui/Select'
import { toast } from '@/ui/toast'
import { useReceiptPrinting } from '../_shared/receiptActions'
import { useDrawerStatus } from '../cash-drawer/useDrawerStatus'
import { productsApi, type ProductListItem } from '../products/api'
import { useSupplier, type SupplierList } from '../suppliers/api'
import { purchaseTotals, useCreatePurchase } from './api'
import styles from './purchases.module.css'

interface Line {
  /** Row identity while editing (the product id: a product can be on the purchase once). */
  productId: number
  name: string
  barcode: string
  unit: string | null
  /** Stock before this purchase, for the "after" column. */
  stock: number
  quantity: number | null
  unitCost: number | null
  updateCost: boolean
  /** Currency of the product's own cost price: when it differs from the supplier's the cost is not pre-filled. */
  productCurrencyId: number
}

const today = () => toDateInputValue(new Date())
/** Products come 20 at a time as the picker list is scrolled. */
const PRODUCT_PAGE = 20

/**
 * `/app/purchases/new` (optionally `?supplier=ID`, which is how the supplier page opens it) - goods received from one supplier.
 * Saving adds every quantity to the product's stock, optionally sets each product's cost price, and puts what was not paid on the
 * supplier's balance, all or nothing on the server. The totals shown here are a preview of the server's arithmetic.
 */
export default function PurchaseFormPage() {
  const { t } = useTranslation(['purchases', 'common'])
  const navigate = useNavigate()
  const [search] = useSearchParams()
  const preset = Number(search.get('supplier'))
  const create = useCreatePurchase()
  const canAddProduct = useCan(Permissions.Products_Create)
  const printing = useReceiptPrinting()
  const drawer = useDrawerStatus()

  const [supplierId, setSupplierId] = useState<number | null>(Number.isInteger(preset) && preset > 0 ? preset : null)
  const supplierQuery = useSupplier(supplierId ?? 0, supplierId !== null)
  const supplier = supplierQuery.data
  // The new product opens in its own window (like "Add product" under a pack) with this supplier already chosen.
  const newProductHref = useHref(supplierId ? `/products/new?supplier=${supplierId}` : '/products/new')
  const [date, setDate] = useState(today)
  const [reference, setReference] = useState('')
  const [notes, setNotes] = useState('')
  // Null until the person touches it: then the supplier's agreed discount is used.
  const [discountInput, setDiscountInput] = useState<number | null | undefined>(undefined)
  const [paid, setPaid] = useState<number | null>(null)
  const [lines, setLines] = useState<Line[]>([])
  const [errors, setErrors] = useState<string[]>([])
  const found = useRef(new Map<number, ProductListItem>())

  const notation = supplier?.currencyNotation ?? ''
  const discount = discountInput === undefined ? (supplier?.discountRate ?? 0) : (discountInput ?? 0)
  const totals = purchaseTotals(
    lines.map((l) => ({ quantity: l.quantity ?? 0, unitCost: l.unitCost ?? 0 })),
    discount,
  )
  const paidNow = paid ?? 0
  // Cash paid at the counter normally comes out of the till: ticked by default while the user's drawer is open, until they untick it.
  const [fromDrawerChoice, setFromDrawerChoice] = useState<boolean | undefined>(undefined)
  const fromDrawer = paidNow > 0 && drawer.isOpen && (fromDrawerChoice ?? true)
  const due = Math.round((totals.total - paidNow) * 100) / 100
  const balanceAfter = supplier ? Math.round((supplier.balance + due) * 100) / 100 : null

  // Stable functions: the pickers reload their list whenever the loader changes.
  const loadSuppliers = useCallback(async (q: string): Promise<SelectOption<number>[]> => {
    const page = await http.get<SupplierList>('/suppliers', { query: { q, page: 1, pageSize: 20, sort: 'name', dir: 'asc' } })
    return page.items.filter((s) => s.isActive).map((s) => ({ value: s.id, label: s.name, hint: s.phone ?? undefined }))
  }, [])

  // Lazy: nothing is requested until the list is opened, then one page at a time as it is scrolled. Only what this supplier
  // sells - a purchase is goods from one supplier, and a product belongs to one supplier.
  const loadProducts = useCallback(
    async (q: string, pageNumber: number) => {
      const page = await productsApi.list({ q, page: pageNumber, pageSize: PRODUCT_PAGE, sort: 'name', dir: 'asc', categoryId: null, status: 'active', stock: 'all', supplierId })
      page.items.forEach((p) => found.current.set(p.id, p))
      return {
        items: page.items.map((p) => ({ value: p.id, label: p.name, hint: `${p.barcode} · ${t('InStock', { qty: formatNumber(p.stockQuantity, 3) })}` })),
        hasMore: page.page * page.pageSize < page.total,
      }
    },
    [supplierId, t],
  )

  const addProduct = (option: SelectOption<number> | null) => {
    const product = option ? found.current.get(option.value) : undefined
    if (!product) return
    setErrors([])
    setLines((current) => {
      // The same product again adds to its row instead of making a second one.
      if (current.some((l) => l.productId === product.id)) {
        return current.map((l) => (l.productId === product.id ? { ...l, quantity: (l.quantity ?? 0) + 1 } : l))
      }
      const sameCurrency = supplier !== undefined && product.currencyId === supplier.currencyId
      return [
        ...current,
        {
          productId: product.id,
          name: product.name,
          barcode: product.barcode,
          unit: product.unit ?? null,
          stock: product.stockQuantity,
          quantity: 1,
          unitCost: sameCurrency && product.purchasePrice > 0 ? product.purchasePrice : null,
          updateCost: true,
          productCurrencyId: product.currencyId,
        },
      ]
    })
  }

  const patch = (productId: number, change: Partial<Line>) =>
    setLines((current) => current.map((l) => (l.productId === productId ? { ...l, ...change } : l)))

  const validate = (): string[] => {
    const problems: string[] = []
    if (supplierId === null) problems.push(t('ErrSupplier'))
    if (lines.length === 0) problems.push(t('ErrNoLines'))
    if (lines.some((l) => !l.quantity || l.quantity <= 0)) problems.push(t('ErrQuantity'))
    if (lines.some((l) => l.unitCost === null || l.unitCost < 0)) problems.push(t('ErrCost'))
    if (paidNow > totals.total) problems.push(t('ErrPaid'))
    if (!date || date > today()) problems.push(t('ErrDate'))
    return problems
  }

  const save = async (andPrint: boolean) => {
    const problems = validate()
    setErrors(problems)
    if (problems.length > 0 || supplierId === null) return
    try {
      const created = await create.mutateAsync({
        supplierId,
        purchaseDate: date,
        supplierReference: reference.trim() || null,
        notes: notes.trim() || null,
        discountPercent: discount,
        amountPaid: paidNow,
        payFromDrawer: fromDrawer,
        lines: lines.map((l) => ({ productId: l.productId, quantity: l.quantity ?? 0, unitCost: l.unitCost ?? 0, updateCostPrice: l.updateCost })),
      })
      toast.success(t('Saved', { number: created.number }))
      if (andPrint) await printing.print(`/purchases/${created.id}/print`, `/purchases/${created.id}/pdf`, `${created.number}.pdf`)
      void navigate(`/purchases/${created.id}`, { replace: true })
    } catch (error) {
      setErrors([isApiError(error) ? error.message : t('common.actionFailed', { ns: 'common' })])
    }
  }

  const busy = create.isPending || printing.busy

  return (
    <div className={styles.page}>
      <PageHeader title={t('NewPurchase')} back={{ to: supplierId ? `/suppliers/${supplierId}` : '/purchases', label: supplierId ? t('BackToSupplier') : t('BackToPurchases') }} />

      <FormError messages={errors} />

      <div className={styles.formLayout}>
        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('PurchaseInformation')}</h2>
            <div className={styles.formGrid}>
              <Field label={t('Supplier')} required>
                {(a11y) => (
                  <Select<number>
                    {...a11y}
                    value={supplierId !== null ? { value: supplierId, label: supplier?.name ?? `#${supplierId}` } : null}
                    onChange={(option) => {
                      setSupplierId(option?.value ?? null)
                      setLines([])
                      setDiscountInput(undefined)
                      setPaid(null)
                    }}
                    loadOptions={loadSuppliers}
                    placeholder={t('ChooseSupplier')}
                  />
                )}
              </Field>
              <Field label={t('PurchaseDate')} required>
                {(a11y) => <input {...a11y} className={field.control} type="date" value={date} max={today()} onChange={(e) => setDate(e.target.value)} />}
              </Field>
              <Field label={t('SupplierReference')} hint={t('SupplierReferenceHint')}>
                {(a11y) => <input {...a11y} className={field.control} value={reference} maxLength={100} onChange={(e) => setReference(e.target.value)} dir="auto" />}
              </Field>
              <Field label={t('Notes')}>
                {(a11y) => <input {...a11y} className={field.control} value={notes} maxLength={500} onChange={(e) => setNotes(e.target.value)} />}
              </Field>
            </div>
            {supplier && supplierId !== null && (
              <p className={styles.hint}>{t('SupplierCurrencyHint', { currency: supplier.currencyName ?? notation })}</p>
            )}
          </section>

          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('Products')}</h2>
            <div className={styles.pickerRow}>
              <div className={styles.pickerField}>
                <Field label={t('AddProduct')} hint={supplierId === null ? t('PickSupplierFirst') : t('AddProductHint')}>
                  {(a11y) => (
                    <Select<number> {...a11y} value={null} onChange={addProduct} loadPage={loadProducts} placeholder={t('SearchProduct')} disabled={supplierId === null} />
                  )}
                </Field>
              </div>
              {canAddProduct && (
                <Button disabled={supplierId === null} onClick={() => window.open(newProductHref, '_blank')}>
                  {t('NewProduct')}
                </Button>
              )}
            </div>

            {lines.length === 0 ? (
              <p className={styles.empty}>{t('NoLinesYet')}</p>
            ) : (
              <div className={styles.lines} role="table" aria-label={t('Products')}>
                <div className={styles.lineHead} role="row">
                  <span role="columnheader">{t('Product')}</span>
                  <span role="columnheader">{t('Quantity')}</span>
                  <span role="columnheader">{t('UnitCost')}</span>
                  <span role="columnheader" className={styles.alignEnd}>{t('LineTotal')}</span>
                  <span role="columnheader" />
                </div>
                {lines.map((l) => {
                  const lineTotal = Math.round((l.quantity ?? 0) * (l.unitCost ?? 0) * 100) / 100
                  const converts = supplier !== undefined && l.productCurrencyId !== supplier.currencyId
                  return (
                    <div key={l.productId} className={styles.line} role="row">
                      <div role="cell" className={styles.lineProduct}>
                        <strong>
                          <bdi>{l.name}</bdi>
                        </strong>
                        <span className={styles.muted}>
                          <bdi>{l.barcode}</bdi> · {t('StockNowAfter', { now: formatNumber(l.stock, 3), after: formatNumber(l.stock + (l.quantity ?? 0), 3) })}
                        </span>
                        <label className={styles.checkLabel}>
                          <input type="checkbox" checked={l.updateCost} onChange={(e) => patch(l.productId, { updateCost: e.target.checked })} />
                          {converts ? t('UpdateCostConverted') : t('UpdateCost')}
                        </label>
                      </div>
                      <div role="cell">
                        <NumericInput
                          value={l.quantity}
                          onChange={(quantity) => patch(l.productId, { quantity })}
                          decimals={3}
                          min={0}
                          suffix={l.unit && l.unit !== 'piece' ? l.unit : undefined}
                          aria-label={`${t('Quantity')}: ${l.name}`}
                          selectOnFocus
                        />
                      </div>
                      <div role="cell">
                        <NumericInput
                          value={l.unitCost}
                          onChange={(unitCost) => patch(l.productId, { unitCost })}
                          decimals={4}
                          min={0}
                          prefix={notation || undefined}
                          aria-label={`${t('UnitCost')}: ${l.name}`}
                          selectOnFocus
                        />
                      </div>
                      <div role="cell" className={`${styles.alignEnd} tabular-nums`}>
                        <strong>{formatMoney(lineTotal, notation)}</strong>
                      </div>
                      <div role="cell">
                        <IconButton icon="trash" label={t('RemoveLine', { name: l.name })} tone="danger" onClick={() => setLines((current) => current.filter((x) => x.productId !== l.productId))} />
                      </div>
                    </div>
                  )
                })}
              </div>
            )}
          </section>
        </div>

        <div className={styles.column}>
          <section className={`${styles.card} ${styles.sticky}`}>
            <h2 className={styles.cardTitle}>{t('PurchaseSummary')}</h2>
            <dl className={styles.summary}>
              <div className={styles.summaryRow}>
                <dt>{t('Subtotal')}</dt>
                <dd className="tabular-nums">{formatMoney(totals.subtotal, notation)}</dd>
              </div>
            </dl>
            <Field label={t('DiscountPercent')}>
              {(a11y) => <NumericInput {...a11y} value={discountInput === undefined ? (supplier?.discountRate ?? null) : discountInput} onChange={setDiscountInput} decimals={2} min={0} max={100} suffix="%" selectOnFocus />}
            </Field>
            <dl className={styles.summary}>
              {totals.discount > 0 && (
                <div className={styles.summaryRow}>
                  <dt>{t('Discount')}</dt>
                  <dd className="tabular-nums">-{formatMoney(totals.discount, notation)}</dd>
                </div>
              )}
              <div className={styles.summaryRow}>
                <dt>{t('Total')}</dt>
                <dd className={`${styles.grand} tabular-nums`}>{formatMoney(totals.total, notation)}</dd>
              </div>
            </dl>
            <Field label={t('PaidNow')} hint={t('PaidNowHint')}>
              {(a11y) => <NumericInput {...a11y} value={paid} onChange={setPaid} decimals={2} min={0} prefix={notation || undefined} selectOnFocus />}
            </Field>
            <Button onClick={() => setPaid(totals.total > 0 ? totals.total : null)} disabled={totals.total <= 0}>
              {t('PayInFull')}
            </Button>
            {paidNow > 0 && drawer.known && (
              <div>
                <label className={styles.checkLabel}>
                  <input type="checkbox" checked={fromDrawer} disabled={!drawer.isOpen} onChange={(e) => setFromDrawerChoice(e.target.checked)} />
                  {t('PayFromDrawer')}
                </label>
                <p className={styles.hint}>{drawer.isOpen ? t('PayFromDrawerHint') : t('DrawerClosedHint')}</p>
              </div>
            )}
            <dl className={styles.summary}>
              <div className={styles.summaryRow}>
                <dt>{t('LeftToPay')}</dt>
                <dd className={styles.money} data-tone={due > 0 ? 'owe' : undefined}>
                  {formatMoney(Math.max(due, 0), notation)}
                </dd>
              </div>
              {balanceAfter !== null && (
                <div className={styles.summaryRow}>
                  <dt>{t('BalanceAfter')}</dt>
                  <dd className={styles.money} data-tone={balanceAfter > 0 ? 'owe' : balanceAfter < 0 ? 'owed' : undefined}>
                    {formatMoney(balanceAfter, notation)}
                  </dd>
                </div>
              )}
            </dl>
            <div className={styles.actions}>
              <Button variant="primary" size="lg" block loading={create.isPending && !printing.busy} disabled={busy} onClick={() => void save(false)}>
                {t('SavePurchase')}
              </Button>
              <Button block loading={printing.busy} disabled={busy} onClick={() => void save(true)}>
                {t('SaveAndPrint')}
              </Button>
              <Button variant="ghost" block disabled={busy} onClick={() => void navigate(-1)}>
                {t('Cancel', { ns: 'common' })}
              </Button>
            </div>
            <p className={styles.hint}>{t('SaveHint')}</p>
          </section>
        </div>
      </div>
    </div>
  )
}
