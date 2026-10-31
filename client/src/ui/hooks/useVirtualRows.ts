import { useCallback, useEffect, useState, type RefObject } from 'react'

export interface RowWindow {
  /** First row index to render. */
  start: number
  /** One past the last row index to render. */
  end: number
  /** Spacer heights (px) standing in for the rows above / below the window. */
  padBefore: number
  padAfter: number
}

/** Pure windowing maths for fixed-height rows — kept separate so it is unit-testable. */
export function computeRowWindow(count: number, rowHeight: number, scrollTop: number, viewportHeight: number, overscan = 6): RowWindow {
  if (count === 0) return { start: 0, end: 0, padBefore: 0, padAfter: 0 }
  const first = Math.floor(scrollTop / rowHeight)
  const visible = Math.ceil(viewportHeight / rowHeight)
  const start = Math.max(0, first - overscan)
  const end = Math.min(count, first + visible + overscan)
  return { start, end, padBefore: start * rowHeight, padAfter: (count - end) * rowHeight }
}

/**
 * Windowing for long fixed-height lists inside a scroll container (`containerRef`). Used by
 * DataTable's `virtualize` option; screens that need a 2-D grid (POS tiles) use TanStack Virtual
 * instead — this hook deliberately stays tiny.
 */
export function useVirtualRows(containerRef: RefObject<HTMLElement | null>, count: number, rowHeight: number, enabled = true, overscan = 6): RowWindow {
  const [metrics, setMetrics] = useState({ scrollTop: 0, height: 600 })

  const measure = useCallback(() => {
    const el = containerRef.current
    if (el) setMetrics({ scrollTop: el.scrollTop, height: el.clientHeight || 600 })
  }, [containerRef])

  useEffect(() => {
    const el = containerRef.current
    if (!enabled || !el) return
    measure()
    let frame = 0
    const onScroll = () => {
      cancelAnimationFrame(frame)
      frame = requestAnimationFrame(measure)
    }
    el.addEventListener('scroll', onScroll, { passive: true })
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(measure) : null
    ro?.observe(el)
    return () => {
      cancelAnimationFrame(frame)
      el.removeEventListener('scroll', onScroll)
      ro?.disconnect()
    }
  }, [containerRef, enabled, measure])

  return enabled
    ? computeRowWindow(count, rowHeight, metrics.scrollTop, metrics.height, overscan)
    : { start: 0, end: count, padBefore: 0, padAfter: 0 }
}
