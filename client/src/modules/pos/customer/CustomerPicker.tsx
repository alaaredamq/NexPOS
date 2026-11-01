import { useCallback, useId, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { Select, type SelectOption } from '@/ui/Select'
import { formatMoney } from '@/ui/format'
import { customerName } from '../checkout/customerNames'
import { searchCustomers, type PosCustomer } from '../checkout/api'
import { QuickCustomerForm } from './QuickCustomerForm'
import styles from '../checkout/checkout.module.css'

interface Props {
  customerId: number | null
  onChange: (customerId: number | null, customer: PosCustomer | null) => void
  /** Notation for the "owes" hint (the default currency: balances are kept in it). */
  notation: string
  disabled?: boolean
}

function toOption(customer: PosCustomer, notation: string, owes: string, credit: string): SelectOption<number> {
  const hint =
    customer.balance > 0 ? `${owes} ${formatMoney(customer.balance, notation)}` : customer.balance < 0 ? `${credit} ${formatMoney(-customer.balance, notation)}` : customer.email || undefined
  return { value: customer.id, label: customer.name, hint }
}

/**
 * The customer on the sale: empty = walk-in. A searchable list that loads the first matches from the server as the cashier types (never the
 * whole customer table), plus an inline "New" form so a first-time customer can be added without leaving, and losing, the cart.
 * The balance shows next to each name because "put it on their account" is decided by who owes what.
 */
export function CustomerPicker({ customerId, onChange, notation, disabled }: Props) {
  const { t } = useTranslation('pos')
  const inputId = useId()
  const canCreate = useCan(Permissions.Customers_Create)
  const [adding, setAdding] = useState(false)
  const [picked, setPicked] = useState<SelectOption<number> | null>(null)

  const owes = t('customerOwes')
  const credit = t('customerCredit')

  const load = useCallback(
    async (query: string) => (await searchCustomers(query.trim())).map((c) => toOption(c, notation, owes, credit)),
    [notation, owes, credit],
  )

  // The option the box shows: the one just picked, or - for a customer that came with a resumed cart - the name remembered for that id.
  const remembered = customerName(customerId)
  const value: SelectOption<number> | null =
    customerId === null ? null : picked?.value === customerId ? picked : { value: customerId, label: remembered ?? t('customerNumber', { id: customerId }) }

  return (
    <div className={styles.fieldGroup}>
      <label htmlFor={inputId} className={styles.fieldLabel}>
        {t('customerOptional')}
      </label>
      <div className={styles.inline}>
        <Select<number>
          id={inputId}
          value={value}
          onChange={(option) => {
            setPicked(option)
            onChange(option?.value ?? null, null)
          }}
          loadOptions={load}
          placeholder={t('walkInCustomer')}
          clearable
          disabled={disabled}
        />
        {canCreate && (
          <Button variant="secondary" onClick={() => setAdding((open) => !open)} disabled={disabled} aria-expanded={adding}>
            {t('newCustomer')}
          </Button>
        )}
      </div>
      {adding && (
        <QuickCustomerForm
          onCancel={() => setAdding(false)}
          onCreated={(customer) => {
            setAdding(false)
            setPicked({ value: customer.id, label: customer.name })
            onChange(customer.id, customer)
          }}
        />
      )}
    </div>
  )
}
