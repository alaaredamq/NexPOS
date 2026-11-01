import { roundHalfEven } from '@/ui/format'

/** A count further than this from the expected cash needs an explanation in the notes (the Razor close page's rule). */
export const NOTES_REQUIRED_DIFFERENCE = 5

export type DifferenceKind = 'surplus' | 'shortage' | 'balanced'

export interface Difference {
  kind: DifferenceKind
  /** Counted minus expected, in cents-rounded money: positive is a surplus. */
  amount: number
}

/**
 * Counted vs expected cash for the close preview. Preview only: the server stores the real difference when the session closes
 * (`CashDifference = ActualCash - ExpectedCash`), and both sides are rounded to cents here so 0.1 + 0.2 never reads as a shortage.
 */
export function differenceOf(expected: number, counted: number): Difference {
  const amount = roundHalfEven(counted - expected, 2) + 0 // + 0 turns a rounded -0 into 0
  return { kind: amount > 0 ? 'surplus' : amount < 0 ? 'shortage' : 'balanced', amount }
}

/** Whether to stop and ask "close anyway?": a big difference with nothing written in the notes. */
export function needsNotesConfirmation(difference: Difference, notes: string): boolean {
  return Math.abs(difference.amount) > NOTES_REQUIRED_DIFFERENCE && notes.trim() === ''
}
