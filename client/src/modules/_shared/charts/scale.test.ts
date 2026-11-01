import { describe, expect, it } from 'vitest'
import { donutArcs, labelIndices, nearestIndex, niceDomain } from './scale'

describe('niceDomain', () => {
  it('starts at zero and ends on a round number above the largest value', () => {
    const d = niceDomain([0, 120, 870, 430])
    expect(d.min).toBe(0)
    expect(d.max).toBeGreaterThanOrEqual(870)
    expect(d.max % 200).toBe(0)
    expect(d.ticks[0]).toBe(0)
    expect(d.ticks.at(-1)).toBe(d.max)
  })

  it('keeps a negative value (a loss) inside the axis', () => {
    const d = niceDomain([-30, 100])
    expect(d.min).toBeLessThanOrEqual(-30)
    expect(d.ticks).toContain(0)
  })

  it('gives an empty series a usable 0 to 1 axis', () => {
    expect(niceDomain([])).toEqual({ min: 0, max: 1, ticks: [0, 1] })
    expect(niceDomain([0, 0])).toEqual({ min: 0, max: 1, ticks: [0, 1] })
  })

  it('does not accumulate float noise in the ticks', () => {
    expect(niceDomain([0.3]).ticks.every((t) => Number.isInteger(t * 100))).toBe(true)
  })
})

describe('nearestIndex', () => {
  it('snaps to the closest of evenly spread points and clamps outside the plot', () => {
    expect(nearestIndex(0, 5, 0, 100)).toBe(0)
    expect(nearestIndex(26, 5, 0, 100)).toBe(1)
    expect(nearestIndex(100, 5, 0, 100)).toBe(4)
    expect(nearestIndex(-40, 5, 0, 100)).toBe(0)
    expect(nearestIndex(500, 5, 0, 100)).toBe(4)
  })

  it('handles a single point', () => expect(nearestIndex(50, 1, 0, 100)).toBe(0))
})

describe('labelIndices', () => {
  it('prints every label when they fit', () => expect(labelIndices(4, 400)).toEqual([0, 1, 2, 3]))

  it('thins labels on a narrow chart, always keeping the first', () => {
    const picks = labelIndices(31, 320)
    expect(picks[0]).toBe(0)
    expect(picks.length).toBeLessThanOrEqual(5)
  })

  it('has nothing to print without points', () => expect(labelIndices(0, 400)).toEqual([]))
})

describe('donutArcs', () => {
  it('splits the circle by share, clockwise from the top', () => {
    const arcs = donutArcs([1, 3])
    expect(arcs[0]).toEqual({ fraction: 0.25, start: 0 })
    expect(arcs[1]).toEqual({ fraction: 0.75, start: 0.25 })
  })

  it('draws nothing when there is no positive value', () => {
    expect(donutArcs([])).toEqual([])
    expect(donutArcs([0, -4])).toEqual([])
  })
})
