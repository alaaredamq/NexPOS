import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadDisplayOrderStrings() {
  await loadNamespaces('productDisplayOrder')
  return null
}

/** `/app/product-display-order` (track C2): the POS main screen's product list. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.MainScreenProducts_Manage} />,
    children: [
      {
        path: 'product-display-order',
        loader: loadDisplayOrderStrings,
        lazy: async () => ({ Component: (await import('./DisplayOrderPage')).default }),
      },
    ],
  },
]

export default routes
