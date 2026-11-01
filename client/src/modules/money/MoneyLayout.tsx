import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { NavLink } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import styles from './money.module.css'

/** The tab bar shared by every page of the module; a tab shows only when the user may open its page. */
export function MoneyLayout({ children }: { children: ReactNode }) {
  const { t } = useTranslation('money')
  const currencies = useCan(Permissions.Currency_View)
  const taxes = useCan(Permissions.Tax_View)
  const discounts = useCan(Permissions.Discounts_View)
  const methods = useCan(Permissions.PaymentMethods_View)
  const lists = useCan([Permissions.Settings_View, Permissions.Settings_Manage])

  const tabs = [
    { to: '/currency', end: true, label: t('Currencies'), show: currencies },
    { to: '/currency/exchange-rates', end: false, label: t('ExchangeRates'), show: currencies },
    { to: '/tax', end: false, label: t('Taxes'), show: taxes },
    { to: '/discounts', end: false, label: t('Discounts'), show: discounts },
    { to: '/payment-methods', end: false, label: t('PaymentMethods'), show: methods },
    { to: '/code-lists', end: false, label: t('CodeLists'), show: lists },
  ].filter((tab) => tab.show)

  return (
    <>
      <nav className={styles.tabs} aria-label={t('MoneyConfiguration')}>
        {tabs.map((tab) => (
          <NavLink key={tab.to} to={tab.to} end={tab.end} className={styles.tab}>
            {tab.label}
          </NavLink>
        ))}
      </nav>
      {children}
    </>
  )
}
