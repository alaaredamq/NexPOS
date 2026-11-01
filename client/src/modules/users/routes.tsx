import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadUserStrings() {
  await loadNamespaces('users')
  return null
}

/** `/app/users` and `/app/users/roles` (track A1). Create / edit are dialogs on those pages, not routes of their own. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Users_View} />,
    children: [
      {
        path: 'users',
        loader: loadUserStrings,
        lazy: async () => ({ Component: (await import('./UsersPage')).default }),
      },
      {
        path: 'users/roles',
        loader: loadUserStrings,
        lazy: async () => ({ Component: (await import('./RolesPage')).default }),
      },
    ],
  },
]

export default routes
