import type { RouteObject } from 'react-router-dom'
import { loadNamespaces } from '@/core/i18n'

/** `/app/_pos-cart` — see CartPreviewPage. Development only, like `/app/_kit`. */
const routes: RouteObject[] = import.meta.env.DEV
  ? [
      {
        path: '_pos-cart',
        loader: async () => {
          await loadNamespaces('pos')
          return null
        },
        lazy: async () => ({ Component: (await import('./CartPreviewPage')).default }),
      },
    ]
  : []

export default routes
