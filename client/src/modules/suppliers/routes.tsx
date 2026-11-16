import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'
import { RequirePlan } from '@/shell/pages/pages'

async function loadSupplierStrings() {
  await loadNamespaces('suppliers', 'purchases')
  return null
}

/** `/app/suppliers` and `/app/suppliers/:id` (track C3). Create and edit are a dialog on those pages, not routes of their own. */
const pages: RouteObject[] = [
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
]

/**
 * A Pro page: on Basic every route of the module shows the upgrade screen instead (the API refuses its calls too). Permission is
 * checked first, so someone who could never use the page sees Forbidden, not an invitation to upgrade.
 */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Suppliers_View} />,
    children: [{ element: <RequirePlan feature="Suppliers" />, children: pages }],
  },
]

export default routes
