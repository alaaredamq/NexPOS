import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { RequirePermission } from '@/core/permissions/permissions'
import { loadNamespaces } from '@/core/i18n'

/**
 * `/app/products` (track C1): list, details, create, edit. The route guards are UX only - every API action carries the
 * same `[RequirePermission]` the old controller had. Pages are lazy so the module costs nothing until it is opened.
 */
const page = (load: () => Promise<{ default: React.ComponentType }>) => async () => ({
  Component: (await load()).default,
  loader: () => loadNamespaces('products', 'common').then(() => null),
})

const routes: RouteObject[] = [
  {
    path: 'products',
    element: <RequirePermission permission={Permissions.Products_View} />,
    children: [
      { index: true, lazy: page(() => import('./ProductsListPage')) },
      {
        element: <RequirePermission permission={Permissions.Products_Create} />,
        children: [{ path: 'new', lazy: page(() => import('./ProductFormPage')) }],
      },
      { path: ':id', lazy: page(() => import('./ProductDetailsPage')) },
      { path: ':id/history', lazy: page(() => import('./ProductHistoryPage')) },
      {
        element: <RequirePermission permission={Permissions.Products_Edit} />,
        children: [{ path: ':id/edit', lazy: page(() => import('./ProductFormPage')) }],
      },
    ],
  },
]

export default routes
