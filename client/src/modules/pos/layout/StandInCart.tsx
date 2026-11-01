import { useEffect, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { formatMoney } from '@/ui/format'
import { posBus, usePosUi, type AddProductRequest } from '../slots'
import styles from './layout.module.css'

interface Line {
  request: AddProductRequest
  quantity: number
}

/**
 * Shown in the cart column until track P3 ships `cart/slot.tsx`: it just echoes what the catalog sent, so the tiles, the search box and
 * the scanner can be tried on their own. It decides nothing (no prices, no totals) and is never used once the real cart exists.
 */
export function StandInCart() {
  const { t } = useTranslation('pos')
  const [lines, setLines] = useState<Line[]>([])
  const setCartCount = usePosUi((s) => s.setCartCount)

  useEffect(
    () =>
      posBus.onAddProduct((request) =>
        setLines((current) => {
          const index = current.findIndex((line) => line.request.product.id === request.product.id)
          if (index < 0) return [...current, { request, quantity: request.quantity }]
          return current.map((line, i) => (i === index ? { ...line, quantity: line.quantity + request.quantity } : line))
        }),
      ),
    [],
  )
  useEffect(() => setCartCount(lines.length), [lines.length, setCartCount])

  return (
    <div className={styles.standIn} data-testid="stand-in-cart">
      <p className={styles.standInNote}>{t('CartPlaceholder')}</p>
      {lines.length === 0 ? (
        <p className={styles.standInEmpty}>{t('StartAddingProductsToBegin')}</p>
      ) : (
        <ul className={styles.standInList}>
          {lines.map(({ request, quantity }) => (
            <li key={request.product.id}>
              <span>{request.product.name}</span>
              <span>
                {quantity} × {formatMoney(request.product.sellingPrice, request.product.currencyNotation)}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
