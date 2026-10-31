import { useTranslation } from 'react-i18next'
import { Icon } from './Icon'
import { formatNumber } from './format'
import styles from './Pagination.module.css'

interface PaginationProps {
  /** 1-based, matching the API's `page`. */
  page: number
  pageSize: number
  total: number
  onPageChange: (page: number) => void
  onPageSizeChange?: (pageSize: number) => void
  pageSizes?: number[]
}

/** Page numbers to show: always first/last, a window around the current page, `null` for an ellipsis gap. */
export function pageWindow(page: number, pageCount: number, siblings = 1): Array<number | null> {
  const wanted = new Set<number>([1, pageCount])
  for (let p = page - siblings; p <= page + siblings; p++) if (p >= 1 && p <= pageCount) wanted.add(p)
  const sorted = [...wanted].sort((a, b) => a - b)
  const out: Array<number | null> = []
  sorted.forEach((p, i) => {
    const prev = sorted[i - 1]
    if (prev !== undefined && p - prev === 2) out.push(prev + 1)
    else if (prev !== undefined && p - prev > 2) out.push(null)
    out.push(p)
  })
  return out
}

/** Server-paging control: "1–25 of 340", page buttons, optional page-size select. Hidden when there is nothing to page. */
export function Pagination({ page, pageSize, total, onPageChange, onPageSizeChange, pageSizes = [10, 25, 50, 100] }: PaginationProps) {
  const { t } = useTranslation()
  const pageCount = Math.max(1, Math.ceil(total / pageSize))
  const current = Math.min(Math.max(1, page), pageCount)
  const first = total === 0 ? 0 : (current - 1) * pageSize + 1
  const last = Math.min(total, current * pageSize)

  if (total === 0) return null

  return (
    <nav className={styles.bar} aria-label={t('common.pagination', 'Pagination')}>
      <span className={styles.summary} aria-live="polite">
        {t('common.pageRange', {
          from: formatNumber(first, 0),
          to: formatNumber(last, 0),
          total: formatNumber(total, 0),
          defaultValue: '{{from}}–{{to}} of {{total}}',
        })}
      </span>

      <div className={styles.pages}>
        <button
          type="button"
          className={styles.btn}
          disabled={current <= 1}
          onClick={() => onPageChange(current - 1)}
          aria-label={t('common.previousPage', 'Previous page')}
        >
          <Icon name="chevron-start" />
        </button>
        {pageWindow(current, pageCount).map((p, i) =>
          p === null ? (
            <span key={`gap-${i}`} className={styles.gap} aria-hidden="true">
              …
            </span>
          ) : (
            <button
              key={p}
              type="button"
              className={`${styles.btn} tabular-nums`}
              data-active={p === current}
              aria-current={p === current ? 'page' : undefined}
              aria-label={t('common.goToPage', { page: p, defaultValue: 'Page {{page}}' })}
              onClick={() => onPageChange(p)}
            >
              {p}
            </button>
          ),
        )}
        <button
          type="button"
          className={styles.btn}
          disabled={current >= pageCount}
          onClick={() => onPageChange(current + 1)}
          aria-label={t('common.nextPage', 'Next page')}
        >
          <Icon name="chevron-end" />
        </button>
      </div>

      {onPageSizeChange && (
        <label className={styles.size}>
          <span>{t('common.rowsPerPage', 'Rows')}</span>
          <select value={pageSize} onChange={(e) => onPageSizeChange(Number(e.target.value))} className={styles.select}>
            {pageSizes.map((s) => (
              <option key={s} value={s}>
                {s}
              </option>
            ))}
          </select>
        </label>
      )}
    </nav>
  )
}
