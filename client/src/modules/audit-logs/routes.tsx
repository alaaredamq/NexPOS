import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadAuditStrings() {
  await loadNamespaces('auditLogs')
  return null
}

/** `/app/audit-logs` (track I3). One page: the details of an entry are a drawer on it (`?entry=<id>`), not a route of their own. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.AuditLogs_View} />,
    children: [
      {
        path: 'audit-logs',
        loader: loadAuditStrings,
        lazy: async () => ({ Component: (await import('./AuditLogsPage')).default }),
      },
    ],
  },
]

export default routes
