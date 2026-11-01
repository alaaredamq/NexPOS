import { i18n } from '@/core/i18n'
import { fetchCurrencies, fetchHeldCart, fetchProduct, type ResumedLine } from './api'
import { useCartStore, lineFromProduct, type CartDiscount, type CartLine, type LinePrice } from '../store/cartStore'
import { rememberCustomer } from '../checkout/customerNames'
import { cleanAmount } from './units'

function priceOf(line: ResumedLine): LinePrice {
  if (line.priceKind === 'tier' && line.priceTierId) {
    return { kind: 'tier', tierId: line.priceTierId, label: line.priceLabel ?? '', amount: line.unitPrice, currencyId: line.currencyId }
  }
  if (line.priceKind === 'custom') return { kind: 'custom', amount: line.unitPrice, currencyId: line.currencyId }
  return { kind: 'default' }
}

export interface ResumeResult {
  /** Lines now on the cart. */
  loaded: number
  /** Names of lines whose product no longer exists / could not be fetched. */
  skipped: string[]
}

/**
 * Puts a held cart back on the till. The held cart keeps no product details, so each line is rebuilt from a fresh product fetch (its
 * unit, price list and stock are today's); how each line was priced is kept (default / stored price / custom) and the server re-prices
 * the cart as soon as it is on the screen. The held cart itself stays in the held list until it is deleted, as in the old till.
 */
export async function resumeHeldCart(id: number, customerName?: string): Promise<ResumeResult> {
  const held = await fetchHeldCart(id)
  const products = await Promise.allSettled(held.lines.map((l) => fetchProduct(l.productId)))

  const lines: CartLine[] = []
  const skipped: string[] = []
  held.lines.forEach((resumed, i) => {
    const fetched = products[i]!
    if (fetched.status === 'rejected') {
      skipped.push(resumed.name)
      return
    }
    const product = fetched.value
    const byAmount = resumed.weightOrVolume != null && product.unitKind !== 'count'
    const line = lineFromProduct(product, {
      quantity: resumed.quantity,
      ...(byAmount ? { weightOrVolume: resumed.weightOrVolume!, displayValue: cleanAmount(resumed.weightOrVolume!), displayUnit: product.unit ?? undefined } : {}),
    })
    lines.push({ ...line, price: product.canChangePrice ? priceOf(resumed) : { kind: 'default' } })
  })

  // A held discount has no currency of its own: a flat amount was typed in the cart currency of that moment, which is the one we are in.
  const currencies = await fetchCurrencies()
  const currencyId = useCartStore.getState().currencyId ?? currencies.find((c) => c.isDefault)?.id ?? currencies[0]?.id ?? 0
  const label = held.discountLabel ?? null
  const discount: CartDiscount | null = held.discountPercentage
    ? { type: 'percentage', value: held.discountPercentage, label }
    : held.discountAmount
      ? { type: 'amount', value: held.discountAmount, currencyId, label }
      : null

  rememberCustomer(held.customerId, customerName)
  useCartStore.getState().load({ lines, discount, customerId: held.customerId ?? null, notes: held.notes }, held.cartId)
  return { loaded: lines.length, skipped }
}

/** The note written on the cart that is parked automatically before another one is loaded. */
export const autoSaveNote = () => i18n.t('heldAutoSaved', { ns: 'pos' })
