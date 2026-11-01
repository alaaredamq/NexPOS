import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { NumericInput } from '@/ui/form/NumericInput'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { formatPercent } from '@/ui/format'
import { toast } from '@/ui/toast'
import { useCartStore, type CartDiscount } from '../store/cartStore'
import { useDiscounts } from './api'
import { money } from './cartUi'
import styles from './cart.module.css'

type Kind = 'percentage' | 'amount' | 'preset'

interface Props {
  currencyId: number
  notation: string
  /** The cart's subtotal as the screen shows it (a flat discount may not exceed it). */
  subtotal: number
  onClose: () => void
}

/** Whole-cart discount: a percentage, a flat amount, or one of the shop's stored discounts. A new one replaces the current one. */
export function DiscountDialog({ currencyId, notation, subtotal, onClose }: Props) {
  const { t } = useTranslation('pos')
  const current = useCartStore((s) => s.discount)
  const applyDiscount = useCartStore((s) => s.applyDiscount)
  const presets = useDiscounts().data ?? []

  const [kind, setKind] = useState<Kind>(current?.type === 'amount' ? 'amount' : 'percentage')
  const [value, setValue] = useState<number | null>(current?.value ?? null)
  const [presetId, setPresetId] = useState<number | null>(null)
  const [tried, setTried] = useState(false)
  const typeId = useId()
  const valueId = useId()
  const presetSelectId = useId()
  const errorId = useId()

  const preset = presets.find((p) => p.id === (presetId ?? presets[0]?.id))

  // What is about to be applied, in the terms of the store.
  const candidate: CartDiscount | null =
    kind === 'preset'
      ? preset
        ? { type: 'percentage', value: preset.rate, label: preset.name }
        : null
      : value !== null && value > 0
        ? kind === 'percentage'
          ? { type: 'percentage', value, label: null }
          : { type: 'amount', value, currencyId, label: null }
        : null

  const error = (() => {
    if (!candidate) return t('pleaseEnterValidDiscount')
    if (candidate.type === 'percentage' && candidate.value > 100) return t('percentageCannotExceed100')
    if (candidate.type === 'amount' && candidate.value > subtotal) return t('discountCannotExceedTotal')
    return null
  })()

  // The discount worked out the other way round, while the cashier types.
  const equivalent =
    candidate && error === null && subtotal > 0
      ? candidate.type === 'percentage'
        ? money((subtotal * candidate.value) / 100, notation)
        : formatPercent((candidate.value / subtotal) * 100, 2)
      : ''

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (error !== null || !candidate) {
      setTried(true)
      return
    }
    applyDiscount(candidate)
    const text = candidate.label ?? (candidate.type === 'percentage' ? formatPercent(candidate.value, 2) : money(candidate.value, notation))
    toast.success(`${t('discountApplied')}: ${text}`)
    onClose()
  }

  const remove = () => {
    applyDiscount(null)
    onClose()
  }

  return (
    <Modal onClose={onClose} width={420} title={t('applyDiscount')}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.dialogForm}>
            <div className={styles.fieldGroup}>
              <label htmlFor={typeId} className={styles.fieldLabel}>
                {t('discountType')}
              </label>
              <select id={typeId} className={styles.select} value={kind} onChange={(e) => setKind(e.target.value as Kind)} data-autofocus>
                <option value="percentage">{t('percentage')}</option>
                <option value="amount">{t('fixedAmount')}</option>
                {presets.length > 0 && <option value="preset">{t('presetDiscount')}</option>}
              </select>
            </div>

            {kind === 'preset' ? (
              <div className={styles.fieldGroup}>
                <label htmlFor={presetSelectId} className={styles.fieldLabel}>
                  {t('selectDiscount')}
                </label>
                <select id={presetSelectId} className={styles.select} value={preset?.id ?? ''} onChange={(e) => setPresetId(Number(e.target.value))}>
                  {presets.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} ({formatPercent(p.rate, 2)})
                    </option>
                  ))}
                </select>
              </div>
            ) : (
              <div className={styles.fieldGroup}>
                <label htmlFor={valueId} className={styles.fieldLabel}>
                  {kind === 'percentage' ? `${t('percentage')} (%)` : `${t('fixedAmount')} (${notation})`}
                </label>
                <NumericInput
                  id={valueId}
                  value={value}
                  onChange={setValue}
                  decimals={2}
                  min={0}
                  suffix={kind === 'percentage' ? '%' : undefined}
                  prefix={kind === 'amount' ? notation : undefined}
                  selectOnFocus
                  aria-invalid={tried && error !== null}
                  aria-describedby={tried && error !== null ? errorId : undefined}
                />
              </div>
            )}

            <p className={styles.hint} aria-live="polite" dir="auto">
              {equivalent && (
                <>
                  {t('discountEquivalent')}: <bdi dir="ltr">{equivalent}</bdi>
                </>
              )}
            </p>
            {tried && error !== null && (
              <p id={errorId} className={styles.fieldError} role="alert">
                {error}
              </p>
            )}
            <div className={styles.callout}>
              <span>{t('subtotal')}</span>
              <strong dir="ltr">{money(subtotal, notation)}</strong>
            </div>
          </div>
        </ModalBody>
        <ModalFooter>
          {current && (
            <Button variant="danger" onClick={remove}>
              {t('discountRemove')}
            </Button>
          )}
          <Button variant="ghost" onClick={onClose}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary">
            {t('Apply')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
