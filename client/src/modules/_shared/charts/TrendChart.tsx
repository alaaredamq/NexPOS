import { useEffect, useId, useMemo, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react'
import { labelIndices, nearestIndex, niceDomain } from './scale'
import styles from './charts.module.css'

export interface TrendSeries {
  key: string
  name: string
  color: string
  /** Second series of a pair is drawn dashed, so the two never rely on colour alone. */
  dashed?: boolean
}

export interface TrendPoint {
  /** Axis text of the point (already formatted for the user's language) - keep it short. */
  label: string
  /** Longer text for the tooltip and the table (`3 Oct 2026`); the axis label when absent. */
  title?: string
  /** One value per series, in the order of `series`. */
  values: number[]
}

interface TrendChartProps {
  series: TrendSeries[]
  points: TrendPoint[]
  /** Full value text, shown in the tooltip (money with its currency). */
  formatValue: (value: number) => string
  /** Short value text for the y-axis ticks. */
  formatTick: (value: number) => string
  /** Describes the chart for screen readers; the same numbers are available in the table view. */
  ariaLabel: string
  height?: number
  /** Dims the chart while a new period loads, without removing it (no layout jump). */
  refreshing?: boolean
}

const PAD = { top: 12, bottom: 28, start: 52, end: 12 }

/**
 * A line chart for a handful of series over an ordered axis (days, months): 2 px lines, recessive grid, a crosshair that snaps to the nearest
 * point with one tooltip listing every series, keyboard arrows, and a legend that names each series. Plain SVG - no chart library - so it
 * costs nothing in the bundle and the axis keeps its left-to-right order inside Arabic pages (dates and money read LTR everywhere).
 */
export function TrendChart({ series, points, formatValue, formatTick, ariaLabel, height = 260, refreshing = false }: TrendChartProps) {
  const titleId = useId()
  const frame = useRef<HTMLDivElement>(null)
  const [width, setWidth] = useState(640)
  const [active, setActive] = useState<number | null>(null)

  useEffect(() => {
    const el = frame.current
    if (!el || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(([entry]) => entry && setWidth(Math.max(240, Math.round(entry.contentRect.width))))
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  const domain = useMemo(() => niceDomain(points.flatMap((p) => p.values)), [points])
  const left = PAD.start
  const right = width - PAD.end
  const top = PAD.top
  const bottom = height - PAD.bottom
  const x = (i: number) => (points.length <= 1 ? (left + right) / 2 : left + ((right - left) * i) / (points.length - 1))
  const y = (v: number) => bottom - ((v - domain.min) / (domain.max - domain.min)) * (bottom - top)
  const clamped = active === null ? null : Math.min(active, points.length - 1)

  const move = (event: PointerEvent<SVGRectElement>) => {
    const box = event.currentTarget.getBoundingClientRect()
    setActive(nearestIndex(event.clientX - box.left + left, points.length, left, right))
  }

  const onKey = (event: KeyboardEvent<SVGSVGElement>) => {
    if (points.length === 0) return
    const step = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
    if (step === 0 && event.key !== 'Escape') return
    event.preventDefault()
    if (event.key === 'Escape') setActive(null)
    else setActive((current) => Math.min(points.length - 1, Math.max(0, (current ?? (step > 0 ? -1 : points.length)) + step)))
  }

  const dir = typeof document === 'undefined' ? 'ltr' : document.documentElement.dir || 'ltr'
  const activePoint = clamped === null ? undefined : points[clamped]

  return (
    <div className={styles.chart} dir="ltr" data-refreshing={refreshing || undefined}>
      <ul className={styles.legend} dir={dir} aria-label={ariaLabel}>
        {series.map((s) => (
          <li key={s.key} className={styles.legendItem}>
            <svg width="22" height="8" aria-hidden="true">
              <line x1="1" x2="21" y1="4" y2="4" stroke={s.color} strokeWidth="2.5" strokeLinecap="round" strokeDasharray={s.dashed ? '5 4' : undefined} />
            </svg>
            <span>{s.name}</span>
          </li>
        ))}
      </ul>

      <div ref={frame} className={styles.frame}>
        <svg
          width={width}
          height={height}
          viewBox={`0 0 ${width} ${height}`}
          role="img"
          aria-labelledby={titleId}
          tabIndex={0}
          onKeyDown={onKey}
          onBlur={() => setActive(null)}
          className={styles.svg}
        >
          <title id={titleId}>{ariaLabel}</title>

          {domain.ticks.map((tick) => (
            <g key={tick}>
              <line x1={left} x2={right} y1={y(tick)} y2={y(tick)} className={tick === 0 ? styles.axis : styles.grid} />
              <text x={left - 8} y={y(tick)} dy="0.32em" textAnchor="end" className={styles.tick}>
                {formatTick(tick)}
              </text>
            </g>
          ))}

          {labelIndices(points.length, right - left).map((i) => (
            <text key={i} x={x(i)} y={height - 8} textAnchor={i === 0 ? 'start' : 'middle'} className={styles.tick}>
              {points[i]?.label}
            </text>
          ))}

          {series.map((s, si) => (
            <polyline
              key={s.key}
              fill="none"
              stroke={s.color}
              strokeWidth="2"
              strokeLinejoin="round"
              strokeLinecap="round"
              strokeDasharray={s.dashed ? '5 4' : undefined}
              points={points.map((p, i) => `${x(i)},${y(p.values[si] ?? 0)}`).join(' ')}
            />
          ))}

          {/* Markers: every point while there are few (a single day would otherwise draw nothing), else just the latest. */}
          {points.length > 0 &&
            series.map((s, si) =>
              (points.length <= 14 ? points.map((_, i) => i) : [points.length - 1]).map((i) => (
                <circle key={`${s.key}-${i}`} cx={x(i)} cy={y(points[i]?.values[si] ?? 0)} r="4" fill={s.color} className={styles.dot} pointerEvents="none" />
              )),
            )}

          {clamped !== null && (
            <g pointerEvents="none">
              <line x1={x(clamped)} x2={x(clamped)} y1={top} y2={bottom} className={styles.crosshair} />
              {series.map((s, si) => (
                <circle key={s.key} cx={x(clamped)} cy={y(points[clamped]?.values[si] ?? 0)} r="4.5" fill={s.color} className={styles.dot} />
              ))}
            </g>
          )}

          <rect x={left} y={top} width={Math.max(0, right - left)} height={Math.max(0, bottom - top)} fill="transparent" onPointerMove={move} onPointerDown={move} onPointerLeave={() => setActive(null)} />
        </svg>

        {activePoint && clamped !== null && (
          <div
            className={styles.tooltip}
            dir={dir}
            role="status"
            style={{ insetInlineStart: Math.min(Math.max(x(clamped) + 12, 0), width - 190), insetBlockStart: top }}
          >
            <div className={styles.tooltipTitle}>{activePoint.title ?? activePoint.label}</div>
            {series.map((s, si) => (
              <div key={s.key} className={styles.tooltipRow}>
                <svg width="14" height="8" aria-hidden="true">
                  <line x1="1" x2="13" y1="4" y2="4" stroke={s.color} strokeWidth="2.5" strokeLinecap="round" strokeDasharray={s.dashed ? '4 3' : undefined} />
                </svg>
                <strong className="tabular-nums">{formatValue(activePoint.values[si] ?? 0)}</strong>
                <span className={styles.tooltipName}>{s.name}</span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
