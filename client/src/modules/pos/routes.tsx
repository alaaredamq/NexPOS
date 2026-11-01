import type { RouteObject } from 'react-router-dom'
import { loadNamespaces } from '@/core/i18n'

async function loadPosStrings() {
  await loadNamespaces('pos', 'common')
  return null
}

/**
 * `/app/pos` (tracks P2-P4). The till is a full-screen page: `layout: 'bare'` renders it behind the sign-in but without the rail and top bar.
 * No permission guard: the old page was open to every signed-in user (the nav entry carries no permission either).
 */
const routes: RouteObject[] = [
  {
    path: 'pos',
    handle: { layout: 'bare' },
    loader: loadPosStrings,
    lazy: async () => ({ Component: (await import('./PosPage')).default }),
  },
]

export default routes
