import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadWalletStrings() {
  await loadNamespaces('wallet')
  return null
}

/** `/app/wallet`: the shop's Whish Money account (every plan; reading needs PaymentMethods.View, changing PaymentMethods.Manage). */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.PaymentMethods_View} />,
    children: [
      {
        path: 'wallet',
        loader: loadWalletStrings,
        lazy: async () => ({ Component: (await import('./WalletPage')).default }),
      },
    ],
  },
]

export default routes
