import { memo, type CSSProperties } from 'react'
import { useTranslation } from 'react-i18next'
import { categoryColor } from '../../_shared/categoryPalette'
import { Icon } from '@/ui/Icon'
import { formatMoney, formatQty } from '@/ui/format'
import type { PosProduct } from './api'
import { stockLevel } from './stock'
import styles from './catalog.module.css'

interface ProductTileProps {
  product: PosProduct
  onPress: (product: PosProduct) => void
}

/**
 * A compact "keycap": name, a dashed tear rule, then price and stock. No picture on purpose (the register is scanned by name and price,
 * and without images the grid fits far more keys). The tint is the category's palette colour, the same one the dashboard donut uses.
 * A sold-out key stays pressable: the cashier may still sell it, and the cart/checkout ask before selling past zero.
 */
export const ProductTile = memo(function ProductTile({ product, onPress }: ProductTileProps) {
  const { t } = useTranslation(['pos', 'common'])
  const level = stockLevel(product)
  const style = { '--tile-accent': categoryColor(product.categoryName) } as CSSProperties
  const unit = product.unitKind === 'count' ? t('units', { ns: 'pos' }) : (product.unit ?? '')

  return (
    <button type="button" className={`${styles.tile} no-select`} style={style} data-stock={level} onClick={() => onPress(product)}>
      <span className={styles.tileName}>
        {product.name}
        {product.isPack && <span className={styles.packBadge}>{t('PackOf', { ns: 'common', 0: product.packSize ?? 0 })}</span>}
      </span>
      <span className={styles.tileFoot}>
        <span className={styles.tilePrice}>{formatMoney(product.sellingPrice, product.currencyNotation)}</span>
        <span className={styles.tileStock} data-level={level}>
          {level === 'ok' ? (
            <>
              <span className={styles.stockDot} aria-hidden="true" />
              <span>{formatQty(product.stockQuantity)}</span>
              <span className={styles.stockUnit}>{unit}</span>
            </>
          ) : (
            <>
              <Icon name={level === 'out' ? 'error' : 'warning'} size={12} />
              <span>{level === 'out' ? t('OutOfStock', { ns: 'common' }) : t('LowStock', { ns: 'common' })}</span>
            </>
          )}
        </span>
      </span>
    </button>
  )
})
