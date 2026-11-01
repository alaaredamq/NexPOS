import type { RouteObject } from 'react-router-dom'
import { loadNamespaces } from '@/core/i18n'

async function loadNotificationStrings() {
  await loadNamespaces('notifications')
  return null
}

/**
 * `/app/notifications` (track I2). The old page needed only a signed-in user, so there is no route permission; the alerts tab and its
 * buttons are gated by `Alerts.*` inside the page, and the API checks the same keys.
 */
const routes: RouteObject[] = [
  {
    path: 'notifications',
    loader: loadNotificationStrings,
    lazy: async () => ({ Component: (await import('./NotificationsPage')).default }),
  },
]

export default routes
