import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadCategoryStrings() {
  await loadNamespaces('categories')
  return null
}

/** `/app/categories` and `/app/categories/:id` (track C2). Create and edit are a dialog on those pages, not routes of their own. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Categories_View} />,
    children: [
      {
        path: 'categories',
        loader: loadCategoryStrings,
        lazy: async () => ({ Component: (await import('./CategoriesPage')).default }),
      },
      {
        path: 'categories/:id',
        loader: loadCategoryStrings,
        lazy: async () => ({ Component: (await import('./CategoryDetailsPage')).default }),
      },
    ],
  },
]

export default routes
