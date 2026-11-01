import { useEffect, useState, type RefObject } from 'react'

/** How many tiles of at least `minTile` px (plus `gap`) fit in `width`; always at least one. */
export function columnsFor(width: number, minTile: number, gap: number): number {
  if (width <= 0) return 1
  return Math.max(1, Math.floor((width + gap) / (minTile + gap)))
}

/** Tracks the scroll box's width so the virtual grid knows its column count (the tiles stretch to fill each row). */
export function useGridColumns(ref: RefObject<HTMLElement | null>, minTile: number, gap: number): number {
  const [columns, setColumns] = useState(4)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    const measure = () => setColumns(columnsFor(el.clientWidth, minTile, gap))
    measure()
    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [ref, minTile, gap])

  return columns
}
