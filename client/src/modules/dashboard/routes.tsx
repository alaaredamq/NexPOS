import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

/** `/app/dashboard` (track I1). The guard is UX only: the API answers 403 without `Dashboard.View`. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Dashboard_View} />,
    children: [
      {
        path: 'dashboard',
        loader: () => loadNamespaces('dashboard', 'orders', 'common').then(() => null),
        lazy: async () => ({ Component: (await import('./DashboardPage')).default }),
      },
    ],
  },
]

export default routes
