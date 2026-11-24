import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'
import { RequirePlan } from '@/shell/pages/pages'

async function loadInventoryStrings() {
  await loadNamespaces('inventory', 'print', 'products')
  return null
}

/** `/app/inventory`, `/app/inventory/low-stock`, `/app/inventory/stock-value` (track C3). Adjusting stock is a dialog on those pages. */
const pages: RouteObject[] = [
  {
    path: 'inventory',
    loader: loadInventoryStrings,
    lazy: async () => ({ Component: (await import('./InventoryPage')).default }),
  },
  {
    path: 'inventory/low-stock',
    loader: loadInventoryStrings,
    lazy: async () => ({ Component: (await import('./LowStockPage')).default }),
  },
  {
    path: 'inventory/stock-value',
    loader: loadInventoryStrings,
    lazy: async () => ({ Component: (await import('./StockValuePage')).default }),
  },
]

/**
 * A Pro page: on Basic every route of the module shows the upgrade screen instead (the API refuses its calls too). Permission is
 * checked first, so someone who could never use the page sees Forbidden, not an invitation to upgrade.
 */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Inventory_View} />,
    children: [{ element: <RequirePlan feature="Inventory" />, children: pages }],
  },
]

export default routes
