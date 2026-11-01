import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { Icon } from '@/ui/Icon'
import { NumericInput } from '@/ui/form/NumericInput'
import { Modal, ModalBody } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { ltrIsolate } from '@/ui/format'
import { useCartStore, type CartLine, type LinePrice } from '../store/cartStore'
import { usePriceOptions } from './api'
import { money } from './cartUi'
import { formatUnit } from './units'
import styles from './cart.module.css'

interface Props {
  line: CartLine
  /** The unit price in force now, cart currency. */
  unitPrice: number
  currencyId: number
  notation: string
  onClose: () => void
}

/**
 * Pick one of the product's prices or type a custom one (the old `PricePicker`). A custom price can never undercut the purchase
 * price: the lowest allowed figure comes from the server (`pos/products/{id}/prices`), which also re-checks it at pricing and sale.
 */
export function PricePickerDialog({ line, unitPrice, currencyId, notation, onClose }: Props) {
  const { t } = useTranslation('pos')
  const options = usePriceOptions(line.productId, currencyId)
  const setPrice = useCartStore((s) => s.setPrice)
  const byWeight = line.weightOrVolume !== null
  const unit = formatUnit(line.unit)

  const [custom, setCustom] = useState<number | null>(line.price.kind === 'custom' ? unitPrice : null)
  const [tried, setTried] = useState(false)
  const inputId = useId()
  const errorId = useId()
  const helpId = useId()

  const data = options.data
  const retail = t('priceRetail')

  const apply = (price: LinePrice, amount: number, label: string) => {
    setPrice(line.productId, price)
    toast.success(t('priceSet', { price: money(amount, notation), label }))
    onClose()
  }

  const error = (() => {
    if (!data) return null
    if (custom === null) return t('priceEnterToContinue')
    if (!(custom > 0)) return t('priceAboveZero')
    if (data.hasCost && custom < data.minPrice) {
      return t('priceBelowMin', { amount: money(custom, notation), min: money(data.minPrice, notation) })
    }
    return null
  })()

  const submitCustom = (event: FormEvent) => {
    event.preventDefault()
    if (error !== null || custom === null) {
      setTried(true)
      return
    }
    apply({ kind: 'custom', amount: custom, currencyId }, custom, t('priceCustom'))
  }

  const subtitle = byWeight ? t('priceChoosePerUnit', { unit }) : line.quantity > 1 ? t('priceChooseEach', { qty: line.quantity }) : t('priceChoose')

  const preview =
    custom !== null && error === null
      ? byWeight
        ? t('priceNewTotal', { total: money(custom * (line.weightOrVolume ?? 0), notation) })
        : line.quantity === 1
          ? t('priceNewTotal', { total: money(custom * line.quantity, notation) })
          : t('priceNewTotalQty', { qty: line.quantity, price: money(custom, notation), total: money(custom * line.quantity, notation) })
      : ''

  return (
    <Modal onClose={onClose} width={400} title={line.name}>
      <ModalBody>
        <div className={styles.dialogForm}>
          <p className={styles.hint}>{subtitle}</p>

          {options.isPending && (
            <div className={styles.skeleton} aria-busy="true" aria-label={t('priceLoading')}>
              <span />
              <span />
              <span />
            </div>
          )}

          {options.isError && (
            <div role="alert" className={styles.notice} data-tone="error">
              <span>{t('priceLoadFailed')}</span>
              <Button size="md" onClick={() => void options.refetch()}>
                {t('priceRetry')}
              </Button>
            </div>
          )}

          {data && (
            <>
              <ul className={styles.tiers}>
                {data.options.map((option, i) => {
                  const base = data.options[0]!
                  const name = i === 0 ? retail : option.name
                  const sub =
                    i === 0
                      ? t('priceDefault')
                      : t('priceVs', {
                          pct: ltrIsolate(`${Math.round((option.price / base.price - 1) * 100) > 0 ? '+' : ''}${Math.round((option.price / base.price - 1) * 100)}%`),
                          name: retail,
                        })
                  const id = option.id ?? null
                  const selected = line.price.kind !== 'custom' && (line.price.kind === 'tier' ? line.price.tierId : null) === id
                  return (
                    <li key={id ?? 'default'}>
                      <button
                        type="button"
                        className={styles.tier}
                        aria-pressed={selected}
                        data-autofocus={selected || undefined}
                        onClick={() =>
                          apply(
                            id === null ? { kind: 'default' } : { kind: 'tier', tierId: id, label: option.name, amount: option.price, currencyId },
                            option.price,
                            id === null ? retail : option.name,
                          )
                        }
                      >
                        <span className={styles.tierCheck} aria-hidden="true">
                          <Icon name="check" size={14} />
                        </span>
                        <span className={styles.tierText}>
                          <span className={styles.tierName}>{name}</span>
                          <span className={styles.tierSub}>{sub}</span>
                        </span>
                        <span className={styles.tierPrice} dir="ltr">
                          {money(option.price, notation)}
                          {byWeight && <small> / {unit}</small>}
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>

              <form onSubmit={submitCustom} noValidate className={styles.custom}>
                <div className={styles.customHead}>
                  <label htmlFor={inputId} className={styles.fieldLabel}>
                    {byWeight ? t('priceCustomLabelPerUnit', { unit }) : t('priceCustomLabel')}
                  </label>
                  {line.price.kind === 'custom' && <span className={styles.inUse}>{t('priceInUse')}</span>}
                </div>
                <div className={styles.inline}>
                  <NumericInput
                    id={inputId}
                    value={custom}
                    onChange={setCustom}
                    decimals={2}
                    min={0}
                    prefix={notation}
                    selectOnFocus
                    placeholder={t('priceEnterAmount')}
                    aria-invalid={tried && error !== null}
                    aria-describedby={`${helpId}${tried && error !== null ? ` ${errorId}` : ''}`}
                  />
                  <Button type="submit" variant="primary">
                    {t('priceUse')}
                  </Button>
                </div>
                <p id={helpId} className={styles.hint}>
                  {data.hasCost ? t('priceMinHelp', { min: `${money(data.minPrice, notation)}${byWeight ? ` / ${unit}` : ''}` }) : t('priceAnyHelp')}
                </p>
                {tried && error !== null && (
                  <p id={errorId} className={styles.fieldError} role="alert">
                    {error}
                  </p>
                )}
                {preview && <p className={styles.hint}>{preview}</p>}
              </form>
            </>
          )}
        </div>
      </ModalBody>
    </Modal>
  )
}
