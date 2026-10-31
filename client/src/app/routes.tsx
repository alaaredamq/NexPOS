import { Navigate, type RouteObject } from 'react-router-dom'
import { RequireAuth } from '@/core/auth/RequireAuth'
import { useSession } from '@/core/auth/useSession'
import { AppShell } from '@/shell/AppShell'
import { landingPath } from '@/shell/nav'
import { ForbiddenPage, LicenseStoppedPage, ModulePlaceholder, NoPagesPage } from '@/shell/pages/pages'

/**
 * Every `modules/<name>/routes.tsx` default-exports a `RouteObject[]`; they are discovered here so
 * adding a module never touches a shared file (REACT_MIGRATION.md §4.5). Module routes should
 * lazy-load their pages (`lazy: () => import(...)`) to keep the shell inside the D12 budget.
 *
 * Paths are relative to the router basename (`/app`): `{ path: 'orders', ... }` is `/app/orders`, the `spaPath` the
 * session payload announces for the Orders page. Put `handle: { layout: 'bare' }` on a route that is a full-screen page
 * (the POS): it is rendered behind the sign-in but without the rail and top bar.
 */
const found = import.meta.glob<{ default: RouteObject[] }>('../modules/*/routes.tsx', { eager: true })

export const moduleRoutes: RouteObject[] = Object.values(found).flatMap((m) => m.default)

export type RouteLayout = 'bare'

const isBare = (route: RouteObject) => (route.handle as { layout?: RouteLayout } | undefined)?.layout === 'bare'

function IndexRedirect() {
  const home = landingPath(useSession())
  return home ? <Navigate to={home} replace /> : <NoPagesPage />
}

/**
 * Public: `/login`. Everything else sits behind `RequireAuth`. Inside the shell, the `*` route is the placeholder for rail
 * entries that have no React page yet; a real module route always outranks it because a splat is the lowest-priority match.
 */
export function buildRoutes(modules: RouteObject[] = moduleRoutes): RouteObject[] {
  return [
    { path: '/login', lazy: async () => ({ Component: (await import('@/shell/pages/LoginPage')).default }) },
    {
      element: <RequireAuth />,
      children: [
        {
          element: <AppShell />,
          children: [
            { index: true, element: <IndexRedirect /> },
            { path: 'forbidden', element: <ForbiddenPage /> },
            { path: 'license-stopped', element: <LicenseStoppedPage /> },
            ...modules.filter((route) => !isBare(route)),
            { path: '*', element: <ModulePlaceholder /> },
          ],
        },
        ...modules.filter(isBare),
      ],
    },
  ]
}
