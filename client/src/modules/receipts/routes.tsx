import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { RequirePermission } from '@/core/permissions/permissions'
import { loadNamespaces } from '@/core/i18n'

/**
 * `/app/receipts` and `/app/receipts/:id` (track S1). The guards are UX only: every API action carries the permission the old
 * controller had (Sales.View for the pages, Sales.Edit for a void). Pages are lazy.
 */
const page = (load: () => Promise<{ default: React.ComponentType }>) => async () => ({
  Component: (await load()).default,
  loader: () => loadNamespaces('receipts', 'common', 'print').then(() => null),
})

const routes: RouteObject[] = [
  {
    path: 'receipts',
    element: <RequirePermission permission={Permissions.Sales_View} />,
    children: [
      { index: true, lazy: page(() => import('./ReceiptsListPage')) },
      { path: ':id', lazy: page(() => import('./ReceiptDetailsPage')) },
    ],
  },
]

export default routes
