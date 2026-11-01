import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadBackupStrings() {
  await loadNamespaces('backup')
  return null
}

/**
 * `/app/backup` and its sections (track A4). Not under `RequireSalesLicense`: a shop whose licence has stopped selling must still be
 * able to take, download and restore backups.
 */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Backup_View} />,
    children: [
      {
        path: 'backup',
        loader: loadBackupStrings,
        lazy: async () => ({ Component: (await import('./BackupLayout')).default }),
        children: [
          { index: true, lazy: async () => ({ Component: (await import('./BackupsPage')).default }) },
          {
            element: <RequirePermission permission={Permissions.Backup_Restore} />,
            children: [{ path: 'restore', lazy: async () => ({ Component: (await import('./RestorePage')).default }) }],
          },
          { path: 'schedules', lazy: async () => ({ Component: (await import('./SchedulesPage')).default }) },
          { path: 'system', lazy: async () => ({ Component: (await import('./SystemPage')).default }) },
        ],
      },
    ],
  },
]

export default routes
