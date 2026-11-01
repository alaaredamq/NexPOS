import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

/**
 * `/app/cash-drawer` (track S2): the drawer, open, close, history and the Z report. The guards are UX only - every API action carries
 * the same `[RequirePermission]` the old controller had: `Sales.View` for everything, plus `Sales.Edit` to close a session or drop cash.
 */
const page = (load: () => Promise<{ default: React.ComponentType }>) => async () => ({
  Component: (await load()).default,
  loader: () => loadNamespaces('cashDrawer', 'common').then(() => null),
})

const routes: RouteObject[] = [
  {
    path: 'cash-drawer',
    element: <RequirePermission permission={Permissions.Sales_View} />,
    children: [
      { index: true, lazy: page(() => import('./DrawerPage')) },
      { path: 'open', lazy: page(() => import('./OpenDrawerPage')) },
      { path: 'history', lazy: page(() => import('./HistoryPage')) },
      { path: 'z-report/:id', lazy: page(() => import('./ZReportPage')) },
      {
        element: <RequirePermission permission={Permissions.Sales_Edit} />,
        children: [
          { path: 'close', lazy: page(() => import('./CloseDrawerPage')) },
          { path: 'close/:id', lazy: page(() => import('./CloseDrawerPage')) },
        ],
      },
    ],
  },
]

export default routes
