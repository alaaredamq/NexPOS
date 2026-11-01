import { useFieldArray, useFormContext } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { IconButton } from '@/ui/IconButton'
import { MoneyField } from '@/ui/form/MoneyField'
import { TextField } from '@/ui/form/TextField'
import type { ProductFormValues } from './form'
import styles from './ProductForm.module.css'

/** The "Extra prices" rows (Wholesale, ...). The selling price stays the default; each extra one needs a name and may not undercut the purchase price. */
export function PriceTiersEditor({ currency }: { currency: string }) {
  const { t } = useTranslation('products')
  const { control } = useFormContext<ProductFormValues>()
  const { fields, append, remove } = useFieldArray({ control, name: 'priceTiers' })

  return (
    <section className={styles.tiers} aria-label={t('Extra prices', { ns: 'common' })}>
      <div className={styles.tiersHead}>
        <h3 className={styles.tiersTitle}>{t('Extra prices', { ns: 'common' })}</h3>
        <Button onClick={() => append({ name: '', price: null })}>{t('Add price', { ns: 'common' })}</Button>
      </div>
      <p className={styles.help}>{t('Extra prices help', { ns: 'common' })}</p>

      {fields.map((field, index) => (
        <div key={field.id} className={styles.tierRow}>
          <TextField<ProductFormValues>
            name={`priceTiers.${index}.name`}
            label={t('Price name', { ns: 'common' })}
            placeholder={t('Price name example', { ns: 'common' })}
            maxLength={50}
          />
          <MoneyField<ProductFormValues> name={`priceTiers.${index}.price`} label={t('Price', { ns: 'common' })} currency={currency} placeholder="0.00" />
          <IconButton icon="trash" tone="danger" label={t('Remove price', { ns: 'common' })} onClick={() => remove(index)} />
        </div>
      ))}
    </section>
  )
}
