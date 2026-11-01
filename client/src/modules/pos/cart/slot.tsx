import { useEffect } from 'react'
import { useTranslation } from 'react-i18next'
import { confirm } from '@/ui/confirm'
import { toast } from '@/ui/toast'
import { posBus, usePosUi } from '../slots'
import { useCartStore } from '../store/cartStore'
import { addToCart } from './addToCart'
import { CartPanel } from './CartPanel'
import { useCartUi } from './cartUi'

/**
 * The cart as the till screen (P2) hosts it: the bridge between `posBus` and the cart. A tile, a scan or a search pick arrives as
 * `addProduct`; the quick-action buttons and shortcuts arrive as commands. Checkout is P4's: `onCheckout` runs the `checkout` command,
 * which stays a no-op until `checkout/slot.tsx` answers it.
 */
export default function CartSlot() {
  const { t } = useTranslation('pos')
  const lineCount = useCartStore((s) => s.lines.length)
  const setCartCount = usePosUi((s) => s.setCartCount)

  useEffect(() => setCartCount(lineCount), [lineCount, setCartCount])

  useEffect(() => {
    const open = useCartUi.getState().open
    const offs = [
      posBus.onAddProduct((request) => void addToCart(request.product, { quantity: request.quantity })),
      posBus.onCommand('hold', () => open({ kind: 'hold' })),
      posBus.onCommand('retrieve', () => open({ kind: 'held' })),
      posBus.onCommand('discount', () => open({ kind: 'discount' })),
      posBus.onCommand('clear', () => {
        if (useCartStore.getState().lines.length === 0) return
        void confirm({ title: t('areYouSure'), message: t('clearAllItems'), confirmLabel: t('yesClearIt'), danger: true }).then((ok) => {
          if (!ok) return
          useCartStore.getState().clear()
          toast.success(t('cartCleared'))
        })
      }),
    ]
    return () => offs.forEach((off) => off())
  }, [t])

  return <CartPanel onCheckout={() => void posBus.run('checkout')} />
}
