import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadCustomerStrings() {
  await loadNamespaces('customers')
  return null
}

/** `/app/customers` and `/app/customers/:id` (track S3). Create and edit are a dialog on those pages, not routes of their own. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Customers_View} />,
    children: [
      {
        path: 'customers',
        loader: loadCustomerStrings,
        lazy: async () => ({ Component: (await import('./CustomersPage')).default }),
      },
      {
        path: 'customers/:id',
        loader: loadCustomerStrings,
        lazy: async () => ({ Component: (await import('./CustomerDetailsPage')).default }),
      },
    ],
  },
]

export default routes
