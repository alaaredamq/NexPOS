import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

/** `/app/reports` (track I1): one page for the old Reports index, the sales report and its daily / monthly / yearly variants. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Reports_View} />,
    children: [
      {
        path: 'reports',
        loader: () => loadNamespaces('reports', 'common').then(() => null),
        lazy: async () => ({ Component: (await import('./ReportsPage')).default }),
      },
    ],
  },
]

export default routes
