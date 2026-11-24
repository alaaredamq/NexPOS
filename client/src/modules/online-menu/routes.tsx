import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadOnlineMenuStrings() {
  await loadNamespaces('onlineMenu')
  return null
}

/** `/app/online-menu`: the sections of the shop's public online menu and what each one shows. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Products_Edit} />,
    children: [
      {
        path: 'online-menu',
        loader: loadOnlineMenuStrings,
        lazy: async () => ({ Component: (await import('./OnlineMenuPage')).default }),
      },
    ],
  },
]

export default routes
