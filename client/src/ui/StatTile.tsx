import styles from './StatTile.module.css'

interface StatTileProps {
  label: string
  /** Pre-formatted (use formatMoney / formatQty) — the tile never formats numbers itself. */
  value: string
  sub?: string
  tone?: 'default' | 'success' | 'danger' | 'info'
}

export function StatTile({ label, value, sub, tone = 'default' }: StatTileProps) {
  return (
    <div className={styles.tile}>
      <div className={styles.label}>{label}</div>
      <div className={`${styles.value} tabular-nums`} data-tone={tone}>
        {value}
      </div>
      {sub && <div className={styles.sub}>{sub}</div>}
    </div>
  )
}
