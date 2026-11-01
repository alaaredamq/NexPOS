import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { TrendChart, type TrendPoint, type TrendSeries } from './TrendChart'
import styles from './charts.module.css'

interface TrendPanelProps {
  series: TrendSeries[]
  points: TrendPoint[]
  formatValue: (value: number) => string
  formatTick: (value: number) => string
  ariaLabel: string
  /** Header of the first table column (the date). */
  labelHeader: string
  refreshing?: boolean
  /** Shown instead of the chart when there are no points. */
  emptyText: string
}

/** The line chart with a switch to the same numbers as a table: every value the tooltip shows is reachable without hovering. */
export function TrendPanel({ series, points, formatValue, formatTick, ariaLabel, labelHeader, refreshing, emptyText }: TrendPanelProps) {
  const { t } = useTranslation('common')
  const [table, setTable] = useState(false)

  if (points.length === 0) return <p className={styles.empty}>{emptyText}</p>

  return (
    <div className={styles.panel}>
      <div className={styles.panelBar}>
        <Button variant="ghost" onClick={() => setTable((on) => !on)} aria-pressed={table}>
          {table ? t('common.showChart') : t('common.showTable')}
        </Button>
      </div>
      {table ? (
        <div className={styles.tableWrap} data-refreshing={refreshing || undefined}>
          <table className={styles.table}>
            <caption className={styles.srOnly}>{ariaLabel}</caption>
            <thead>
              <tr>
                <th scope="col">{labelHeader}</th>
                {series.map((s) => (
                  <th key={s.key} scope="col" className={styles.num}>
                    {s.name}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {points.map((p, i) => (
                <tr key={`${p.label}-${i}`}>
                  <th scope="row">{p.title ?? p.label}</th>
                  {series.map((s, si) => (
                    <td key={s.key} className={`${styles.num} tabular-nums`}>
                      {formatValue(p.values[si] ?? 0)}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <TrendChart series={series} points={points} formatValue={formatValue} formatTick={formatTick} ariaLabel={ariaLabel} refreshing={refreshing} />
      )}
    </div>
  )
}
