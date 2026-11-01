import type { RouteObject } from 'react-router-dom'
import { loadNamespaces } from '@/core/i18n'

async function loadAccountStrings() {
  await loadNamespaces('account')
  return null
}

/**
 * `/app/account/profile` and `/app/account/change-pin` (track A1). Open to every signed-in user: nobody needs a permission to look
 * after their own account, and a user whose PIN an admin just set has to reach the second page whatever their role grants.
 */
const routes: RouteObject[] = [
  {
    path: 'account/profile',
    loader: loadAccountStrings,
    lazy: async () => ({ Component: (await import('./ProfilePage')).default }),
  },
  {
    path: 'account/change-pin',
    loader: loadAccountStrings,
    lazy: async () => ({ Component: (await import('./ChangePinPage')).default }),
  },
]

export default routes
