import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { RequirePermission } from '@/core/permissions/permissions'

/**
 * `/app/license` (track A4). Not under `RequireSalesLicense`: this is the screen that fixes a licence that stopped selling, so it must open
 * exactly then. Its texts are the `common` namespace (`License_*`), which is always loaded.
 */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.License_Manage} />,
    children: [{ path: 'license', lazy: async () => ({ Component: (await import('./LicensePage')).default }) }],
  },
]

export default routes
