import { formatMoney, ltrIsolate } from '@/ui/format'
import { DRAWER_CURRENCY } from './api'

/** `$ 1,234.50`; a negative amount carries its minus inside the left-to-right isolate, so it stays beside the number in Arabic text. */
export const money = (amount: number) => formatMoney(amount, DRAWER_CURRENCY)

/** Like `money`, with an explicit `+` for a positive amount (differences, adjustments). */
export const signedMoney = (amount: number) => (amount > 0 ? ltrIsolate(`+${money(amount)}`) : money(amount))
