import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'
import { RequirePlan } from '@/shell/pages/pages'

const page = (load: () => Promise<{ default: React.ComponentType }>) => async () => ({
  Component: (await load()).default,
  loader: () => loadNamespaces('purchases', 'common', 'print').then(() => null),
})

/**
 * `/app/purchases`, `/app/purchases/new` and `/app/purchases/:id`. The guards are UX only: every API action carries its own
 * permission (Purchases.View to read and print, Purchases.Create to save, Purchases.Cancel to undo).
 */
/**
 * A Pro page: on Basic every route of the module shows the upgrade screen instead (the API refuses its calls too). Permission is
 * checked first, so someone who could never use the page sees Forbidden, not an invitation to upgrade.
 */
const routes: RouteObject[] = [
  {
    path: 'purchases',
    element: <RequirePermission permission={Permissions.Purchases_View} />,
    children: [
      {
        element: <RequirePlan feature="Purchases" />,
        children: [
          { index: true, lazy: page(() => import('./PurchasesPage')) },
          {
            element: <RequirePermission permission={Permissions.Purchases_Create} />,
            children: [{ path: 'new', lazy: page(() => import('./PurchaseFormPage')) }],
          },
          { path: ':id', lazy: page(() => import('./PurchaseDetailsPage')) },
        ],
      },
    ],
  },
]

export default routes
