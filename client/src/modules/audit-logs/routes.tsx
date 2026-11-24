import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'
import { RequirePlan } from '@/shell/pages/pages'

async function loadAuditStrings() {
  await loadNamespaces('auditLogs')
  return null
}

/** `/app/audit-logs` (track I3). One page: the details of an entry are a drawer on it (`?entry=<id>`), not a route of their own. */
const pages: RouteObject[] = [
  {
    path: 'audit-logs',
    loader: loadAuditStrings,
    lazy: async () => ({ Component: (await import('./AuditLogsPage')).default }),
  },
]

/**
 * A Pro page: on Basic every route of the module shows the upgrade screen instead (the API refuses its calls too). Permission is
 * checked first, so someone who could never use the page sees Forbidden, not an invitation to upgrade.
 */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.AuditLogs_View} />,
    children: [{ element: <RequirePlan feature="AuditLog" />, children: pages }],
  },
]

export default routes
