import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadSupplierStrings() {
  await loadNamespaces('suppliers', 'purchases')
  return null
}

/** `/app/suppliers` and `/app/suppliers/:id` (track C3). Create and edit are a dialog on those pages, not routes of their own. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Suppliers_View} />,
    children: [
      {
        path: 'suppliers',
        loader: loadSupplierStrings,
        lazy: async () => ({ Component: (await import('./SuppliersPage')).default }),
      },
      {
        path: 'suppliers/:id',
        loader: loadSupplierStrings,
        lazy: async () => ({ Component: (await import('./SupplierDetailsPage')).default }),
      },
    ],
  },
]

export default routes
