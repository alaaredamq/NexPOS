import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from '@/ui/Icon'
import { formatQty } from '@/ui/format'
import { useCartStore, type CartLine } from '../store/cartStore'
import { money, requestAmount, useCartUi } from './cartUi'
import type { LineView } from './useCartTotals'
import { formatUnit } from './units'
import styles from './cart.module.css'

interface Props {
  line: CartLine
  view: LineView | undefined
  notation: string
  currencyId: number | null
  /** The server has priced this exact cart; until then the amounts are estimates. */
  settled: boolean
}

/** Pieces as an editable number: type 24 and press Enter, or use the steppers. Commits on blur / Enter; Escape restores. */
function QuantityInput({ line }: { line: CartLine }) {
  const { t } = useTranslation('pos')
  const setQty = useCartStore((s) => s.setQty)
  // What is being typed, tied to the quantity it started from: when the quantity changes underneath (a stepper, a scan) the draft is dropped.
  const [typed, setTyped] = useState<{ text: string; from: number } | null>(null)
  const draft = typed && typed.from === line.quantity ? typed.text : null
  const setDraft = (text: string | null) => setTyped(text === null ? null : { text, from: line.quantity })

  const commit = () => {
    if (draft === null) return
    const parsed = Number.parseInt(draft.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660)), 10)
    setDraft(null)
    if (Number.isFinite(parsed) && parsed !== line.quantity) setQty(line.productId, parsed)
  }

  return (
    <div className={styles.stepper}>
      <button type="button" aria-label={t('decreaseQuantity', { name: line.name })} onClick={() => setQty(line.productId, line.quantity - 1)}>
        <Icon name="minus" size={14} />
      </button>
      <input
        className={styles.qtyInput}
        inputMode="numeric"
        dir="ltr"
        aria-label={t('quantity')}
        value={draft ?? String(line.quantity)}
        onChange={(e) => setDraft(e.target.value)}
        onFocus={(e) => e.currentTarget.select()}
        onBlur={commit}
        onKeyDown={(e) => {
          if (e.key === 'Enter') {
            e.preventDefault()
            commit()
          } else if (e.key === 'Escape' && draft !== null) {
            e.stopPropagation()
            setDraft(null)
          }
        }}
      />
      <button type="button" aria-label={t('increaseQuantity', { name: line.name })} onClick={() => setQty(line.productId, line.quantity + 1)}>
        <Icon name="plus" size={14} />
      </button>
    </div>
  )
}

export function CartLineRow({ line, view, notation, currencyId, settled }: Props) {
  const { t } = useTranslation('pos')
  const remove = useCartStore((s) => s.remove)
  const open = useCartUi((s) => s.open)

  const byWeight = line.weightOrVolume !== null
  const unitPrice = view?.unitPrice ?? 0
  const priceLabel = line.price.kind === 'custom' ? t('priceCustom') : line.price.kind === 'tier' ? line.price.label : t('priceRetail')
  const total = money(view?.lineTotal ?? 0, notation)

  const editAmount = async () => {
    if (currencyId === null) return
    const entry = await requestAmount({
      product: { id: line.productId, name: line.name, unit: line.unit },
      unitPrice,
      notation,
      initial: { displayValue: line.displayValue ?? line.weightOrVolume ?? 0, displayUnit: line.displayUnit ?? line.unit ?? '' },
    })
    if (entry && entry.weightOrVolume !== undefined) {
      useCartStore.getState().setWeight(line.productId, entry.displayValue ?? entry.weightOrVolume, entry.displayUnit ?? line.unit ?? '')
    }
  }

  const each = byWeight
    ? t('pricePerUnit', { price: money(unitPrice, notation), unit: formatUnit(line.unit) })
    : line.quantity > 1
      ? t('priceEach', { price: money(unitPrice, notation) })
      : null

  return (
    <li className={styles.row} data-estimate={settled ? undefined : true}>
      <div className={styles.rowName}>
        <span className={styles.name}>{line.name}</span>
        {each && (
          <span className={styles.each} dir="ltr">
            {each}
          </span>
        )}
        {view?.insufficientStock && (
          <span className={styles.stockWarning} role="status">
            <Icon name="warning" size={13} /> {t('cartLowStock', { stock: formatQty(view.stockQuantity ?? 0) })}
          </span>
        )}
      </div>

      <div className={styles.rowQty}>
        {byWeight ? (
          <button type="button" className={styles.amountBtn} onClick={() => void editAmount()} aria-label={t('cartEditAmount', { name: line.name })}>
            <span dir="ltr">{formatQty(line.displayValue ?? line.weightOrVolume ?? 0)}</span>
            <small>{formatUnit(line.displayUnit ?? line.unit)}</small>
            <Icon name="edit" size={13} />
          </button>
        ) : (
          <QuantityInput line={line} />
        )}
      </div>

      <div className={styles.rowPrice}>
        {line.canChangePrice ? (
          <button
            type="button"
            className={styles.priceBtn}
            data-kind={line.price.kind}
            aria-haspopup="dialog"
            aria-label={t('priceButtonLabel', { amount: total, label: priceLabel, name: line.name })}
            onClick={() => open({ kind: 'price', productId: line.productId })}
          >
            <span className={styles.priceTotal} dir="ltr">
              {total}
            </span>
            <span className={styles.priceTier}>
              {line.price.kind === 'custom' && <Icon name="edit" size={11} />}
              <span>{priceLabel}</span>
              <Icon name="chevron-down" size={11} />
            </span>
          </button>
        ) : (
          <span className={styles.priceText} dir="ltr">
            {total}
          </span>
        )}
      </div>

      <button type="button" className={styles.del} aria-label={t('removeItem', { name: line.name })} onClick={() => remove(line.productId)}>
        <Icon name="trash" size={16} />
      </button>
    </li>
  )
}
