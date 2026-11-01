import { useCallback, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { LicenseBanner } from '@/shell/LicenseBanner'
import { usePosCategories, type PosProduct } from './catalog/api'
import { CategoryChips } from './catalog/CategoryChips'
import { ProductGrid } from './catalog/ProductGrid'
import { PosTopBar } from './layout/PosTopBar'
import { QuickActions } from './layout/QuickActions'
import { ShortcutsDialog } from './layout/ShortcutsDialog'
import { CartSlot, CheckoutSlot } from './layout/slotRegistry'
import { useShortcuts } from './layout/useShortcuts'
import { ProductSearch } from './search/ProductSearch'
import { PriceCheckDialog } from './scan/PriceCheckDialog'
import { ScanBar } from './scan/ScanBar'
import { posBus } from './slots'
import styles from './layout/layout.module.css'

/**
 * The till (route `/app/pos`, full screen, no rail). Two columns that mirror in Arabic by themselves: the cart (scan bar on top, then
 * the P3 cart slot) and the catalog (search, category chips, virtual tile grid, quick actions). P4's checkout slot hosts the dialogs.
 * Narrow screens stack the columns. See slots.ts for how the other tracks plug in.
 */
export default function PosPage() {
  const { t } = useTranslation('pos')
  const cartColumn = useRef<HTMLDivElement>(null)
  const [categoryId, setCategoryId] = useState<number | null>(null)
  const [mainScreenConfigured, setMainScreenConfigured] = useState(true)
  const [priceCheck, setPriceCheck] = useState(false)
  const [shortcuts, setShortcuts] = useState(false)
  const categories = usePosCategories()

  const openPriceCheck = useCallback(() => setPriceCheck(true), [])
  const closePriceCheck = useCallback(() => {
    setPriceCheck(false)
    posBus.focusScan()
  }, [])
  const closeShortcuts = useCallback(() => {
    setShortcuts(false)
    posBus.focusScan()
  }, [])
  const press = useCallback((product: PosProduct) => void posBus.addProduct({ product, quantity: 1, source: 'tile' }), [])

  useShortcuts(openPriceCheck)

  return (
    <div className={styles.page}>
      <PosTopBar onShowShortcuts={() => setShortcuts(true)} onReviewCart={() => cartColumn.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })} />
      <LicenseBanner />
      <main className={styles.frame}>
        <div ref={cartColumn} className={styles.cartColumn}>
          <ScanBar />
          <div className={styles.cartSlot}>
            <CartSlot />
          </div>
        </div>

        <section className={styles.catalogColumn} aria-label={t('ProductCatalog')}>
          <ProductSearch />
          <CategoryChips
            categories={categories.data ?? []}
            selected={categoryId}
            onSelect={setCategoryId}
            mainScreenConfigured={mainScreenConfigured}
          />
          <ProductGrid categoryId={categoryId} onPress={press} onMainScreenKind={setMainScreenConfigured} />
          <QuickActions onPriceCheck={openPriceCheck} />
        </section>
      </main>

      {CheckoutSlot && <CheckoutSlot />}
      {priceCheck && <PriceCheckDialog onClose={closePriceCheck} />}
      {shortcuts && <ShortcutsDialog onClose={closeShortcuts} />}
    </div>
  )
}
