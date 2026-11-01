import type { ComponentType } from 'react'
import { StandInCart } from './StandInCart'

/**
 * Finds the cart (P3) and checkout (P4) components without anyone editing the page: each track adds one file and this picks it up.
 * See `../slots.ts` for the contract. A glob over a path that does not exist yet is simply empty.
 */
const carts = import.meta.glob<{ default: ComponentType }>('../cart/slot.tsx', { eager: true })
const checkouts = import.meta.glob<{ default: ComponentType }>('../checkout/slot.tsx', { eager: true })

export const CartSlot: ComponentType = Object.values(carts)[0]?.default ?? StandInCart
export const CheckoutSlot: ComponentType | null = Object.values(checkouts)[0]?.default ?? null
