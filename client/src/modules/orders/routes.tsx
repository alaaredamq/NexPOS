import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { RequirePermission } from '@/core/permissions/permissions'
import { loadNamespaces } from '@/core/i18n'

/**
 * `/app/orders` and `/app/orders/:id` (track S1). The guards are UX only: every API action carries the permission the old
 * controller had (Orders.View for the pages, Orders.Edit for a status change). Pages are lazy.
 */
const page = (load: () => Promise<{ default: React.ComponentType }>) => async () => ({
  Component: (await load()).default,
  loader: () => loadNamespaces('orders', 'common', 'print').then(() => null),
})

const routes: RouteObject[] = [
  {
    path: 'orders',
    element: <RequirePermission permission={Permissions.Orders_View} />,
    children: [
      { index: true, lazy: page(() => import('./OrdersListPage')) },
      { path: ':id', lazy: page(() => import('./OrderDetailsPage')) },
    ],
  },
]

export default routes
