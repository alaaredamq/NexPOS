import type { TFunction } from 'i18next'
import type { SelectOption } from '@/ui/Select'

/** What the drop dialog offers. The *value* is the English text the Razor page posted and the Z report prints, so old and new rows read alike. */
const REASONS = [
  { value: 'Safe Deposit', key: 'SafeDeposit' },
  { value: 'Bank Deposit', key: 'BankDeposit' },
  { value: 'Excess Cash', key: 'ExcessCash' },
  { value: 'Other', key: 'Other' },
] as const

export function dropReasonOptions(t: TFunction): SelectOption<string>[] {
  return REASONS.map((r) => ({ value: r.value, label: t(r.key, { ns: 'cashDrawer' }) }))
}

/** A stored reason in the user's language; anything not on the list (older free text) is shown as it was typed. */
export function dropReasonLabel(t: TFunction, reason: string | null | undefined): string {
  if (!reason) return '-'
  const known = REASONS.find((r) => r.value === reason)
  return known ? t(known.key, { ns: 'cashDrawer' }) : reason
}
