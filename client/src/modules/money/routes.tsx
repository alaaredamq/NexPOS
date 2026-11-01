import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadMoneyStrings() {
  await loadNamespaces('money')
  return null
}

/**
 * Money configuration (track A3). The rail has two entries, Currency (`/app/currency`) and Tax (`/app/tax`); every page of the module shares one
 * tab bar, so discounts, payment methods and code lists (which never had a rail entry or a screen) are one click from either.
 * Create / edit are dialogs on these pages, not routes of their own.
 */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Currency_View} />,
    children: [
      {
        path: 'currency',
        loader: loadMoneyStrings,
        lazy: async () => ({ Component: (await import('./CurrenciesPage')).default }),
      },
      {
        path: 'currency/exchange-rates',
        loader: loadMoneyStrings,
        lazy: async () => ({ Component: (await import('./ExchangeRatesPage')).default }),
      },
    ],
  },
  {
    element: <RequirePermission permission={Permissions.Tax_View} />,
    children: [
      {
        path: 'tax',
        loader: loadMoneyStrings,
        lazy: async () => ({ Component: (await import('./TaxesPage')).default }),
      },
    ],
  },
  {
    element: <RequirePermission permission={Permissions.Discounts_View} />,
    children: [
      {
        path: 'discounts',
        loader: loadMoneyStrings,
        lazy: async () => ({ Component: (await import('./DiscountsPage')).default }),
      },
    ],
  },
  {
    element: <RequirePermission permission={Permissions.PaymentMethods_View} />,
    children: [
      {
        path: 'payment-methods',
        loader: loadMoneyStrings,
        lazy: async () => ({ Component: (await import('./PaymentMethodsPage')).default }),
      },
    ],
  },
  {
    element: <RequirePermission permission={[Permissions.Settings_View, Permissions.Settings_Manage]} />,
    children: [
      {
        path: 'code-lists',
        loader: loadMoneyStrings,
        lazy: async () => ({ Component: (await import('./CodeListsPage')).default }),
      },
    ],
  },
]

export default routes
