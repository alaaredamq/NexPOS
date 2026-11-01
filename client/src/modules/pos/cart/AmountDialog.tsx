import { useId, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { NumericInput } from '@/ui/form/NumericInput'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { formatQty } from '@/ui/format'
import { money, settleAmount, type AmountRequest } from './cartUi'
import { availableUnits, convertUnit, defaultEntryUnit, formatUnit, normalizeUnit, unitFamily } from './units'
import styles from './cart.module.css'

/**
 * "How much?" for a product sold by weight or volume (the old `showQuantityInputModal`). The cashier types the amount in any unit of
 * the family (300 g); the line stores it in the product's unit (0.3 kg) because the unit price is per product unit.
 */
export function AmountDialog({ request }: { request: AmountRequest }) {
  const { t } = useTranslation('pos')
  const { product, unitPrice, notation, initial } = request
  const productUnit = normalizeUnit(product.unit)
  const units = availableUnits(product.unit)
  const isWeight = unitFamily(product.unit) === 'weight'

  const [unit, setUnit] = useState(initial?.displayUnit && units.includes(normalizeUnit(initial.displayUnit)) ? normalizeUnit(initial.displayUnit) : defaultEntryUnit(product.unit))
  const [value, setValue] = useState<number | null>(initial?.displayValue ?? null)
  const [tried, setTried] = useState(false)
  const inputId = useId()
  const errorId = useId()

  const base = value !== null && value > 0 ? convertUnit(value, unit, productUnit) : 0
  const invalid = !(value !== null && value > 0)
  const what = isWeight ? t('amountWeight') : t('amountVolume')

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (invalid || value === null) {
      setTried(true)
      return
    }
    settleAmount({ weightOrVolume: base, displayValue: value, displayUnit: unit })
  }

  return (
    <Modal onClose={() => settleAmount(null)} width={420} title={product.name}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.dialogForm}>
            <div className={styles.callout}>
              <span>{t('amountPricePer', { unit: formatUnit(productUnit) })}</span>
              <strong dir="ltr">{money(unitPrice, notation)}</strong>
            </div>

            <div className={styles.fieldGroup}>
              <label htmlFor={inputId} className={styles.fieldLabel}>
                {what}
              </label>
              <div className={styles.inline}>
                <NumericInput
                  id={inputId}
                  value={value}
                  onChange={setValue}
                  decimals={3}
                  min={0}
                  autoFocus
                  selectOnFocus
                  aria-invalid={tried && invalid}
                  aria-describedby={tried && invalid ? errorId : undefined}
                  placeholder={isWeight ? t('amountPlaceholderWeight') : t('amountPlaceholderVolume')}
                />
                <select className={styles.select} value={unit} onChange={(e) => setUnit(e.target.value)} aria-label={t('amountUnit')}>
                  {units.map((u) => (
                    <option key={u} value={u}>
                      {formatUnit(u)}
                    </option>
                  ))}
                </select>
              </div>
              {tried && invalid && (
                <p id={errorId} className={styles.fieldError} role="alert">
                  {t('amountInvalid', { what: what.toLowerCase() })}
                </p>
              )}
            </div>

            <div className={styles.callout} data-tone="total" aria-live="polite">
              <span>{t('amountTotal')}</span>
              <strong dir="ltr">{money(unitPrice * base, notation)}</strong>
            </div>
            {base > 0 && unit !== productUnit && (
              <p className={styles.hint} dir="ltr">
                {formatQty(value ?? 0)} {formatUnit(unit)} = {formatQty(base)} {formatUnit(productUnit)}
              </p>
            )}
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={() => settleAmount(null)}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary">
            {initial ? t('amountUpdate') : t('amountAdd')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
