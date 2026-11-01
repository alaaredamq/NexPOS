import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Loader } from '@/ui/Loader'
import { Modal } from '@/ui/Modal'
import { ScanInput } from '@/ui/ScanInput'
import { formatMoney, formatPercent, formatQty } from '@/ui/format'
import { usePosSearch, type PosProduct } from '../catalog/api'
import { stockLevel } from '../catalog/stock'
import styles from './scan.module.css'

interface PriceCheckDialogProps {
  onClose: () => void
}

/**
 * "What does this cost?" without touching the cart: scan or type a barcode (an exact barcode answers straight away) or a name (pick from
 * the matches). The purchase price is never shown here; a cashier does not need it, and the API does not send it.
 */
export function PriceCheckDialog({ onClose }: PriceCheckDialogProps) {
  const { t } = useTranslation(['pos', 'common'])
  const [value, setValue] = useState('')
  const [term, setTerm] = useState('')
  const [picked, setPicked] = useState<PosProduct | null>(null)
  const search = usePosSearch(term)
  const inputRef = useRef<HTMLInputElement>(null)

  useEffect(() => inputRef.current?.focus(), [])

  const items = useMemo(() => search.data?.pages.flatMap((page) => page.items) ?? [], [search.data])
  const exact = search.data?.pages[0]?.searchType === 'barcode'
  // An exact barcode, or a name that matches a single product, answers straight away; several matches are listed to pick from.
  const product = picked ?? (exact || items.length === 1 ? items[0] : undefined) ?? null

  const submit = () => {
    const next = value.trim()
    setPicked(null)
    setTerm(next)
    setValue('')
  }

  return (
    <Modal onClose={onClose} width={520} title={t('PriceCheck', { ns: 'pos' })}>
      <div className={styles.priceCheck}>
        <ScanInput ref={inputRef} value={value} onChange={setValue} onEnter={submit} size="lg" placeholder={t('scanOrEnterBarcode', { ns: 'pos' })} />

        {term && search.isPending && <Loader fullSection />}
        {term && search.isError && (
          <p className={styles.priceCheckMessage} role="alert">
            {isApiError(search.error) ? search.error.message : t('failedToCheckPrice', { ns: 'pos' })}
          </p>
        )}
        {term && search.isSuccess && items.length === 0 && (
          <p className={styles.priceCheckMessage} role="status">
            {t('productNotFound', { ns: 'pos' })}
          </p>
        )}

        {!product && items.length > 1 && (
          <ul className={styles.matches}>
            {items.map((item) => (
              <li key={item.id}>
                <button type="button" className={styles.match} onClick={() => setPicked(item)}>
                  <span>{item.name}</span>
                  <span className={styles.matchPrice}>{formatMoney(item.sellingPrice, item.currencyNotation)}</span>
                </button>
              </li>
            ))}
          </ul>
        )}

        {product && (
          <section className={styles.result} aria-label={product.name}>
            <h3 className={styles.resultName}>{product.name}</h3>
            <div className={styles.resultPrice}>{formatMoney(product.sellingPrice, product.currencyNotation)}</div>
            <dl className={styles.facts}>
              <dt>{t('barcode', { ns: 'pos' })}</dt>
              <dd dir="ltr">{product.barcode || '—'}</dd>
              <dt>{t('category', { ns: 'pos' })}</dt>
              <dd>{product.categoryName || '—'}</dd>
              <dt>{t('stock', { ns: 'pos' })}</dt>
              <dd data-level={stockLevel(product)}>
                {stockLevel(product) === 'out'
                  ? t('OutOfStock', { ns: 'common' })
                  : `${formatQty(product.stockQuantity)} ${product.unitKind === 'count' ? t('units', { ns: 'pos' }) : (product.unit ?? '')}`}
              </dd>
              {product.supplierDiscountRate > 0 && (
                <>
                  <dt>{t('supplierDiscount', { ns: 'pos' })}</dt>
                  <dd>{formatPercent(product.supplierDiscountRate)}</dd>
                </>
              )}
            </dl>
          </section>
        )}
      </div>
    </Modal>
  )
}
