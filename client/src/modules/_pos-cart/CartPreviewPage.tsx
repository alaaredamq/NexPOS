import { useQuery } from '@tanstack/react-query'
import { http } from '@/api/http'
import { toast } from '@/ui/toast'
import { addToCart } from '../pos/cart/addToCart'
import { CartPanel } from '../pos/cart/CartPanel'
import type { PosProduct } from '../pos/store/cartStore'
import styles from './preview.module.css'

/**
 * `/app/_pos-cart` — development-only host for the cart while the real till screen (P2) is being built: a plain list of
 * products to tap and the real `<CartPanel />` beside it. It is what the P3 Playwright spec and the screenshots drive.
 * Registered in development only (see routes.tsx), so it never reaches a counter PC. P2 can delete it once the till exists.
 */
export default function CartPreviewPage() {
  const products = useQuery({
    queryKey: ['pos', 'preview-products'],
    queryFn: ({ signal }) => http.get<{ items: PosProduct[] }>('/pos/products', { query: { pageSize: 60 }, signal }),
  })

  return (
    <div className={styles.page}>
      <ul className={styles.products} aria-label="Products">
        {products.data?.items.map((p) => (
          <li key={p.id}>
            <button type="button" className={styles.tile} onClick={() => void addToCart(p)}>
              <strong>{p.name}</strong>
              <span>
                {p.currencyNotation} {p.sellingPrice}
                {p.unitKind !== 'count' ? ` / ${p.unit}` : ''}
              </span>
            </button>
          </li>
        ))}
      </ul>
      <div className={styles.cart}>
        <CartPanel onCheckout={() => toast.info('Checkout is track P4')} />
      </div>
    </div>
  )
}
