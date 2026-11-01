import { useState } from 'react'
import { donutArcs } from './scale'
import styles from './charts.module.css'

export interface DonutSlice {
  name: string
  value: number
  color: string
  /** Text for the share, already formatted (`34.5%`). */
  shareText: string
  /** Text for the amount, already formatted (money). */
  valueText: string
}

interface DonutChartProps {
  slices: DonutSlice[]
  ariaLabel: string
  /** Shown in the middle when nothing is hovered. */
  centerLabel: string
  centerValue: string
}

const SIZE = 168
const STROKE = 26
const RADIUS = (SIZE - STROKE) / 2
const CIRCUMFERENCE = 2 * Math.PI * RADIUS
/** Space between neighbouring slices, in px along the ring. */
const GAP = 2

/**
 * One whole split into parts, with a legend that carries the names and figures (colour alone never identifies a slice: a category's hue
 * is derived from its name and seven hues are shared by up to ten categories). Hovering a slice or a legend row highlights the slice
 * and shows its share in the middle. Plain SVG, no library.
 */
export function DonutChart({ slices, ariaLabel, centerLabel, centerValue }: DonutChartProps) {
  const [active, setActive] = useState<number | null>(null)
  const arcs = donutArcs(slices.map((s) => s.value))
  const picked = active === null ? undefined : slices[active]

  return (
    <div className={styles.donut}>
      <svg width={SIZE} height={SIZE} viewBox={`0 0 ${SIZE} ${SIZE}`} role="img" aria-label={ariaLabel} className={styles.donutSvg}>
        <g transform={`rotate(-90 ${SIZE / 2} ${SIZE / 2})`}>
          {arcs.map((arc, i) => {
            const slice = slices[i]
            if (!slice) return null
            const length = Math.max(0, arc.fraction * CIRCUMFERENCE - (arcs.length > 1 ? GAP : 0))
            return (
              <circle
                key={`${slice.name}-${i}`}
                cx={SIZE / 2}
                cy={SIZE / 2}
                r={RADIUS}
                fill="none"
                stroke={slice.color}
                strokeWidth={active === i ? STROKE + 4 : STROKE}
                strokeDasharray={`${length} ${CIRCUMFERENCE - length}`}
                strokeDashoffset={-arc.start * CIRCUMFERENCE}
                opacity={active === null || active === i ? 1 : 0.45}
                onPointerEnter={() => setActive(i)}
                onPointerLeave={() => setActive(null)}
              />
            )
          })}
        </g>
        <text x="50%" y="46%" textAnchor="middle" className={styles.donutValue}>
          {picked ? picked.shareText : centerValue}
        </text>
        <text x="50%" y="46%" dy="1.5em" textAnchor="middle" className={styles.donutLabel}>
          {picked ? picked.name : centerLabel}
        </text>
      </svg>

      <ul className={styles.donutLegend}>
        {slices.map((slice, i) => (
          <li
            key={`${slice.name}-${i}`}
            className={styles.donutRow}
            data-active={active === i || undefined}
            onPointerEnter={() => setActive(i)}
            onPointerLeave={() => setActive(null)}
          >
            <span className={styles.swatch} style={{ background: slice.color }} aria-hidden="true" />
            <span className={styles.donutName} title={slice.name}>
              <bdi>{slice.name}</bdi>
            </span>
            <span className={`${styles.donutShare} tabular-nums`}>{slice.shareText}</span>
            <span className={`${styles.donutAmount} tabular-nums`}>{slice.valueText}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
