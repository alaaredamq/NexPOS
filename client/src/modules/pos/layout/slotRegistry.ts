import type { ComponentType } from 'react'
import CartSlotComponent from '../cart/slot'

/**
 * The cart (P3) is imported directly: it exists. The checkout (P4) is found with `import.meta.glob` so that its track only has to add
 * `checkout/slot.tsx` (a glob over a path that does not exist yet is simply empty). See `../slots.ts` for the contract.
 */
const checkouts = import.meta.glob<{ default: ComponentType }>('../checkout/slot.tsx', { eager: true })

export const CartSlot: ComponentType = CartSlotComponent
export const CheckoutSlot: ComponentType | null = Object.values(checkouts)[0]?.default ?? null
