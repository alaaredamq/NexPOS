import { describe, expect, it } from 'vitest'
import { differenceOf, needsNotesConfirmation, NOTES_REQUIRED_DIFFERENCE } from './drawerMath'

describe('differenceOf', () => {
  it('counted above expected is a surplus, below is a shortage, equal is balanced', () => {
    expect(differenceOf(76.4, 80)).toEqual({ kind: 'surplus', amount: 3.6 })
    expect(differenceOf(76.4, 70)).toEqual({ kind: 'shortage', amount: -6.4 })
    expect(differenceOf(76.4, 76.4)).toEqual({ kind: 'balanced', amount: 0 })
  })

  it('rounds to cents so floating point noise never reads as a shortage', () => {
    // 0.1 + 0.2 = 0.30000000000000004 in binary floating point
    expect(differenceOf(0.1 + 0.2, 0.3)).toEqual({ kind: 'balanced', amount: 0 })
    expect(differenceOf(100.1, 100.1 + 1e-9).kind).toBe('balanced')
  })

  it('counting nothing against a float is a shortage of the whole float', () => {
    expect(differenceOf(50, 0)).toEqual({ kind: 'shortage', amount: -50 })
  })
})

describe('needsNotesConfirmation', () => {
  it('asks only for a difference beyond the threshold, in either direction, with empty notes', () => {
    expect(needsNotesConfirmation(differenceOf(100, 100 + NOTES_REQUIRED_DIFFERENCE), '')).toBe(false) // exactly 5.00 is fine
    expect(needsNotesConfirmation(differenceOf(100, 105.01), '')).toBe(true)
    expect(needsNotesConfirmation(differenceOf(100, 94.99), '')).toBe(true)
    expect(needsNotesConfirmation(differenceOf(100, 100), '')).toBe(false)
  })

  it('a note, even a short one, is enough; blank spaces are not a note', () => {
    expect(needsNotesConfirmation(differenceOf(100, 50), 'counted twice')).toBe(false)
    expect(needsNotesConfirmation(differenceOf(100, 50), '   ')).toBe(true)
  })
})
