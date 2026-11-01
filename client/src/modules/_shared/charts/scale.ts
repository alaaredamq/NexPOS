/** Pure chart maths (no DOM), so the SVG components stay thin and this is unit-testable. */

export interface Domain {
  min: number
  max: number
  ticks: number[]
}

/** Round `value` up to 1, 2, 2.5, 5 or 10 times a power of ten. */
function niceStep(rough: number): number {
  if (rough <= 0) return 1
  const power = 10 ** Math.floor(Math.log10(rough))
  const fraction = rough / power
  const nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10
  return nice * power
}

/**
 * The y axis: always includes 0 (a bar or area chart that does not start at zero lies; the line chart keeps the same rule so profit
 * and sales share one honest baseline), ends on a round number and has about `target` gridlines.
 */
export function niceDomain(values: readonly number[], target = 4): Domain {
  const finite = values.filter(Number.isFinite)
  const low = Math.min(0, ...finite)
  const high = Math.max(0, ...finite)
  if (high === 0 && low === 0) return { min: 0, max: 1, ticks: [0, 1] }

  const step = niceStep((high - low) / target)
  const min = Math.floor(low / step) * step
  const max = Math.ceil(high / step) * step
  const ticks: number[] = []
  for (let v = min; v <= max + step / 2; v += step) ticks.push(Math.round(v * 1e6) / 1e6)
  return { min, max, ticks }
}

/** Index of the point nearest to `x` among `count` points spread evenly from `left` to `right`. */
export function nearestIndex(x: number, count: number, left: number, right: number): number {
  if (count <= 1 || right <= left) return 0
  const ratio = (x - left) / (right - left)
  return Math.min(count - 1, Math.max(0, Math.round(ratio * (count - 1))))
}

/** Indices of the x labels to print so they never collide: about one per `minGap` pixels, always including the first. */
export function labelIndices(count: number, width: number, minGap = 84): number[] {
  if (count <= 0) return []
  const fit = Math.max(1, Math.floor(width / minGap))
  const every = Math.max(1, Math.ceil(count / fit))
  const out: number[] = []
  for (let i = 0; i < count; i += every) out.push(i)
  return out
}

export interface DonutArc {
  /** Fraction of the circle, 0 to 1. */
  fraction: number
  /** Start of the arc as a fraction of the circle, clockwise from 12 o'clock. */
  start: number
}

/** Arcs for a donut: shares of the positive values, in the order given. A total of 0 gives no arcs. */
export function donutArcs(values: readonly number[]): DonutArc[] {
  const positive = values.map((v) => (v > 0 ? v : 0))
  const total = positive.reduce((sum, v) => sum + v, 0)
  if (total <= 0) return []
  let start = 0
  return positive.map((v) => {
    const fraction = v / total
    const arc = { fraction, start }
    start += fraction
    return arc
  })
}
