import type { RouteObject } from 'react-router-dom'

/**
 * `/app/_kit` — component gallery owned by 0C (`ui/kit`). Registered in development only, so it is
 * never part of a production bundle or reachable on a counter PC.
 */
const routes: RouteObject[] = import.meta.env.DEV
  ? [{ path: '_kit', lazy: async () => ({ Component: (await import('@/ui/kit/KitPage')).default }) }]
  : []

export default routes
