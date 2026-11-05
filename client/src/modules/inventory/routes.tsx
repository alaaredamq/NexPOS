import type { RouteObject } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { loadNamespaces } from '@/core/i18n'
import { RequirePermission } from '@/core/permissions/permissions'

async function loadInventoryStrings() {
  await loadNamespaces('inventory', 'print', 'products')
  return null
}

/** `/app/inventory`, `/app/inventory/low-stock`, `/app/inventory/stock-value` (track C3). Adjusting stock is a dialog on those pages. */
const routes: RouteObject[] = [
  {
    element: <RequirePermission permission={Permissions.Inventory_View} />,
    children: [
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
    ],
  },
]

export default routes
