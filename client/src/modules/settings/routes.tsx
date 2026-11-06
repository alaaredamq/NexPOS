import { Navigate, type RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadSettingsStrings() {
  await loadNamespaces('settings')
  return null
}

/**
 * `/app/settings/{store,printers,branding}` (track A2). The Razor landing page of three cards is the tab bar of the module instead,
 * so `/app/settings` itself (the nav entry) goes to the first tab. Everything needs `Settings.Manage`, as the Razor controller did.
 */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Settings_Manage} />,
    children: [
      { path: 'settings', element: <Navigate to="/settings/store" replace /> },
      {
        path: 'settings/store',
        loader: loadSettingsStrings,
        lazy: async () => ({ Component: (await import('./StoreSettingsPage')).default }),
      },
      {
        path: 'settings/printers',
        loader: loadSettingsStrings,
        lazy: async () => ({ Component: (await import('./PrintersPage')).default }),
      },
      {
        path: 'settings/branding',
        loader: loadSettingsStrings,
        lazy: async () => ({ Component: (await import('./BrandingPage')).default }),
      },
      {
        path: 'settings/online',
        loader: loadSettingsStrings,
        lazy: async () => ({ Component: (await import('./OnlinePage')).default }),
      },
    ],
  },
]

export default routes
