import { isApiError } from '@/api/http'
import { i18n } from '@/core/i18n'
import { toast } from '@/ui/toast'
import { useCartStore, type PosProduct } from '../store/cartStore'
import { fetchCurrencies } from './api'
import { requestAmount } from './cartUi'
import { convertAmount } from './pricing'

async function cartCurrencyId(): Promise<{ id: number | null; currencies: Awaited<ReturnType<typeof fetchCurrencies>> }> {
  const currencies = await fetchCurrencies()
  const id = useCartStore.getState().currencyId ?? currencies.find((c) => c.isDefault)?.id ?? currencies[0]?.id ?? null
  return { id, currencies }
}

async function addSingle(product: PosProduct, quantity: number, name?: string): Promise<boolean> {
  const { addProduct } = useCartStore.getState()

  if (product.unitKind === 'count') {
    addProduct(product, { quantity, name })
    if (quantity > 1) toast.success(i18n.t('addedMultipleItems', { ns: 'pos', quantity, name: name ?? product.name }))
    return true
  }

  // Sold by weight or volume: ask how much first (the old till opened the same prompt). The prompt shows the cart currency's price.
  const { id, currencies } = await cartCurrencyId()
  const unitPrice = id === null ? product.sellingPrice : convertAmount(product.sellingPrice, product.currencyId, id, currencies)
  const notation = currencies.find((c) => c.id === (id ?? product.currencyId))?.notation ?? product.currencyNotation
  const entry = await requestAmount({ product: { id: product.id, name: name ?? product.name, unit: product.unit ?? null }, unitPrice, notation })
  if (!entry) return false // the cashier cancelled
  addProduct(product, { ...entry, name })
  return true
}

/**
 * Puts a catalogue product on the cart — the one entry point for tiles, search results and scans (P2).
 *  - a product sold by weight or volume asks for the amount first (cancel = nothing added);
 *  - a pack is its own product: its own line, price and stock (it is not redirected to the single product it contains);
 *  - `quantity` is the scanner's `3*` prefix.
 * Resolves true when something was added. Needs `<CartPanel />` mounted (it renders the amount prompt).
 */
export async function addToCart(product: PosProduct, options: { quantity?: number } = {}): Promise<boolean> {
  const quantity = Math.max(1, Math.floor(options.quantity ?? 1))
  try {
    return await addSingle(product, quantity)
  } catch (error) {
    toast.error(isApiError(error) ? error.message : i18n.t('errorAddingToCart', { ns: 'pos' }))
    return false
  }
}

/** Hook form of `addToCart`, for components that prefer one (the function never changes). */
export function useAddToCart(): typeof addToCart {
  return addToCart
}
