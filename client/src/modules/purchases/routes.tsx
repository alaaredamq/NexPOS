import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

const page = (load: () => Promise<{ default: React.ComponentType }>) => async () => ({
  Component: (await load()).default,
  loader: () => loadNamespaces('purchases', 'common', 'print').then(() => null),
})

/**
 * `/app/purchases`, `/app/purchases/new` and `/app/purchases/:id`. The guards are UX only: every API action carries its own
 * permission (Purchases.View to read and print, Purchases.Create to save, Purchases.Cancel to undo).
 */
const routes: RouteObject[] = [
  {
    path: 'purchases',
    element: <RequirePermission permission={Permissions.Purchases_View} />,
    children: [
      { index: true, lazy: page(() => import('./PurchasesPage')) },
      {
        element: <RequirePermission permission={Permissions.Purchases_Create} />,
        children: [{ path: 'new', lazy: page(() => import('./PurchaseFormPage')) }],
      },
      { path: ':id', lazy: page(() => import('./PurchaseDetailsPage')) },
    ],
  },
]

export default routes
