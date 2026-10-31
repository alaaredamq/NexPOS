import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from './Icon'
import { IconButton } from './IconButton'
import { Skeleton } from './Skeleton'
import { openContextMenu, type ContextMenuEntry } from './ContextMenu'
import { useVirtualRows } from './hooks/useVirtualRows'
import styles from './DataTable.module.css'

export interface Column<T> {
  /** Stable id: sort key sent to the API, and storage key for the column chooser. */
  key: string
  header: ReactNode
  cell: (row: T, index: number) => ReactNode
  sortable?: boolean
  /** Text alignment: `end` for numbers/money so figures line up in both directions. */
  align?: 'start' | 'end' | 'center'
  width?: string | number
  /** Hide this column below a viewport width (px) — keeps tables usable at tablet width. */
  hideBelow?: number
  /** Column cannot be hidden in the chooser. */
  required?: boolean
  /** Render the cell content as monospace tabular numbers (SKUs, order numbers). */
  mono?: boolean
}

export interface SortState {
  key: string
  dir: 'asc' | 'desc'
}

export interface DataTableProps<T> {
  columns: Column<T>[]
  rows: T[]
  rowKey: (row: T) => string | number
  /** Accessible table name (also the visible caption if `showCaption`). */
  caption: string
  loading?: boolean
  /** Shown instead of rows when the query failed — typically the localized ProblemDetails message. */
  error?: string | null
  onRetry?: () => void
  /** Empty-state copy (when not loading and no rows). */
  emptyTitle?: string
  emptyMessage?: string
  emptyAction?: ReactNode

  sort?: SortState | null
  onSortChange?: (sort: SortState | null) => void

  /** Per-row action buttons, rendered in a trailing column (IconButtons). */
  rowActions?: (row: T) => ReactNode
  rowActionsLabel?: string
  onRowClick?: (row: T) => void
  selectedKey?: string | number | null
  /** Right-click menu entries for a row; empty/undefined → no menu. */
  rowMenu?: (row: T) => ContextMenuEntry[]

  /** Persist hidden columns under this localStorage key and show the column chooser button. */
  columnChooserKey?: string

  /** Window the rows (fixed `rowHeight` px) inside a scroll box of `maxHeight` — for lists of hundreds of rows on one page. */
  virtualize?: { rowHeight: number; maxHeight: number }
  /** Number of skeleton rows while the first load is in flight. */
  skeletonRows?: number
  /** Dim the rows while a refetch (new page / sort) is in flight, instead of replacing them with skeletons. */
  refreshing?: boolean
  /** Rendered under the table — normally `<Pagination />`. */
  footer?: ReactNode
  /** Rendered above the table, left of the column chooser — normally filters. */
  toolbar?: ReactNode
}

function useHiddenColumns(storageKey: string | undefined) {
  const [hidden, setHidden] = useState<string[]>(() => {
    if (!storageKey) return []
    try {
      return JSON.parse(localStorage.getItem(storageKey) ?? '[]') as string[]
    } catch {
      return []
    }
  })
  useEffect(() => {
    if (storageKey) localStorage.setItem(storageKey, JSON.stringify(hidden))
  }, [storageKey, hidden])
  return [hidden, setHidden] as const
}

function useBelowWidths(widths: number[]): Set<number> {
  const key = widths.join(',')
  const [below, setBelow] = useState<Set<number>>(() => new Set(widths.filter((w) => typeof window !== 'undefined' && window.innerWidth < w)))
  useEffect(() => {
    const list = key ? key.split(',').map(Number) : []
    const update = () => setBelow(new Set(list.filter((w) => window.innerWidth < w)))
    update()
    window.addEventListener('resize', update)
    return () => window.removeEventListener('resize', update)
  }, [key])
  return below
}

/**
 * Server-driven table: the parent owns page/sort/search state (usually mirrored in the URL) and passes
 * the current page of rows in. Handles loading skeletons, error + retry, empty state, sortable headers
 * (`aria-sort`), row actions, click/keyboard row activation, context menus, an optional column chooser
 * and optional windowing. Replaces the Razor `DataTable` ViewComponent.
 */
export function DataTable<T>({
  columns,
  rows,
  rowKey,
  caption,
  loading = false,
  error,
  onRetry,
  emptyTitle,
  emptyMessage,
  emptyAction,
  sort,
  onSortChange,
  rowActions,
  rowActionsLabel,
  onRowClick,
  selectedKey,
  rowMenu,
  columnChooserKey,
  virtualize,
  skeletonRows = 6,
  refreshing = false,
  footer,
  toolbar,
}: DataTableProps<T>) {
  const { t } = useTranslation()
  const [hidden, setHidden] = useHiddenColumns(columnChooserKey)
  const [chooserOpen, setChooserOpen] = useState(false)
  const chooserId = useId()
  const scrollRef = useRef<HTMLDivElement>(null)
  const narrow = useBelowWidths(columns.flatMap((c) => (c.hideBelow ? [c.hideBelow] : [])))

  const visible = useMemo(
    () => columns.filter((c) => (c.required || !hidden.includes(c.key)) && !(c.hideBelow && narrow.has(c.hideBelow))),
    [columns, hidden, narrow],
  )
  const win = useVirtualRows(scrollRef, rows.length, virtualize?.rowHeight ?? 44, !!virtualize && !loading)

  const colCount = visible.length + (rowActions ? 1 : 0)
  const showSkeleton = loading && rows.length === 0
  const showEmpty = !loading && !error && rows.length === 0

  const toggleSort = (col: Column<T>) => {
    if (!col.sortable || !onSortChange) return
    if (sort?.key !== col.key) onSortChange({ key: col.key, dir: 'asc' })
    else if (sort.dir === 'asc') onSortChange({ key: col.key, dir: 'desc' })
    else onSortChange(null)
  }

  const renderRow = (row: T, index: number) => {
    const key = rowKey(row)
    const clickable = !!onRowClick
    return (
      <tr
        key={key}
        className={styles.row}
        data-clickable={clickable || undefined}
        data-selected={selectedKey === key || undefined}
        tabIndex={clickable ? 0 : undefined}
        aria-selected={selectedKey === undefined ? undefined : selectedKey === key}
        style={virtualize ? { blockSize: virtualize.rowHeight } : undefined}
        onClick={clickable ? () => onRowClick(row) : undefined}
        onKeyDown={
          clickable
            ? (e) => {
                if (e.target === e.currentTarget && (e.key === 'Enter' || e.key === ' ')) {
                  e.preventDefault()
                  onRowClick(row)
                }
              }
            : undefined
        }
        onContextMenu={rowMenu ? (e) => openContextMenu(e, rowMenu(row)) : undefined}
      >
        {visible.map((col) => (
          <td key={col.key} className={col.mono ? `${styles.cell} mono` : styles.cell} data-align={col.align}>
            {col.cell(row, index)}
          </td>
        ))}
        {rowActions && (
          <td className={`${styles.cell} ${styles.actions}`} onClick={(e) => e.stopPropagation()} onKeyDown={(e) => e.stopPropagation()}>
            <div className={styles.actionsInner}>{rowActions(row)}</div>
          </td>
        )}
      </tr>
    )
  }

  const toolbarVisible = !!toolbar || !!columnChooserKey

  return (
    <section className={styles.card} aria-busy={loading || refreshing || undefined}>
      {toolbarVisible && (
        <div className={styles.toolbar}>
          <div className={styles.toolbarMain}>{toolbar}</div>
          {columnChooserKey && (
            <div className={styles.chooserWrap}>
              <IconButton
                icon="columns"
                label={t('common.columns', 'Columns')}
                active={chooserOpen}
                aria-expanded={chooserOpen}
                aria-controls={chooserId}
                onClick={() => setChooserOpen((o) => !o)}
              />
              {chooserOpen && (
                <div id={chooserId} className={styles.chooser} role="group" aria-label={t('common.columns', 'Columns')}>
                  {columns.map((col) => (
                    <label key={col.key} className={styles.chooserItem}>
                      <input
                        type="checkbox"
                        checked={col.required || !hidden.includes(col.key)}
                        disabled={col.required}
                        onChange={(e) => setHidden((h) => (e.target.checked ? h.filter((k) => k !== col.key) : [...h, col.key]))}
                      />
                      <span>{col.header}</span>
                    </label>
                  ))}
                </div>
              )}
            </div>
          )}
        </div>
      )}

      <div
        ref={scrollRef}
        className={styles.scroll}
        data-refreshing={refreshing || undefined}
        style={virtualize ? { maxBlockSize: virtualize.maxHeight } : undefined}
      >
        <table className={styles.table}>
          <caption className="sr-only">{caption}</caption>
          <thead>
            <tr>
              {visible.map((col) => {
                const active = sort?.key === col.key
                const ariaSort = !col.sortable ? undefined : active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : 'none'
                return (
                  <th
                    key={col.key}
                    scope="col"
                    className={styles.th}
                    data-align={col.align}
                    aria-sort={ariaSort}
                    style={col.width !== undefined ? { inlineSize: col.width } : undefined}
                  >
                    {col.sortable && onSortChange ? (
                      <button type="button" className={styles.sortBtn} data-active={active || undefined} onClick={() => toggleSort(col)}>
                        <span>{col.header}</span>
                        <Icon name={active && sort.dir === 'desc' ? 'sort-desc' : 'sort-asc'} size={14} />
                      </button>
                    ) : (
                      col.header
                    )}
                  </th>
                )
              })}
              {rowActions && (
                <th scope="col" className={styles.th} data-align="end">
                  <span className="sr-only">{rowActionsLabel ?? t('common.actions', 'Actions')}</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {showSkeleton &&
              Array.from({ length: skeletonRows }).map((_, i) => (
                <tr key={`sk-${i}`} className={styles.row}>
                  {Array.from({ length: colCount }).map((__, c) => (
                    <td key={c} className={styles.cell}>
                      <Skeleton width={c === 0 ? '70%' : '50%'} />
                    </td>
                  ))}
                </tr>
              ))}

            {error && !loading && (
              <tr>
                <td colSpan={colCount} className={styles.state}>
                  <div className={styles.stateBody} role="alert">
                    <Icon name="error" size={22} />
                    <div className={styles.stateTitle}>{error}</div>
                    {onRetry && (
                      <button type="button" className={styles.retry} onClick={onRetry}>
                        {t('common.retry', 'Try again')}
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            )}

            {showEmpty && (
              <tr>
                <td colSpan={colCount} className={styles.state}>
                  <div className={styles.stateBody}>
                    <div className={styles.stateTitle}>{emptyTitle ?? t('common.noResults', 'Nothing here yet')}</div>
                    {emptyMessage && <div className={styles.stateMessage}>{emptyMessage}</div>}
                    {emptyAction}
                  </div>
                </td>
              </tr>
            )}

            {!error && rows.length > 0 && (
              <>
                {win.padBefore > 0 && (
                  <tr aria-hidden="true">
                    <td colSpan={colCount} style={{ blockSize: win.padBefore, padding: 0, border: 0 }} />
                  </tr>
                )}
                {rows.slice(win.start, win.end).map((row, i) => renderRow(row, win.start + i))}
                {win.padAfter > 0 && (
                  <tr aria-hidden="true">
                    <td colSpan={colCount} style={{ blockSize: win.padAfter, padding: 0, border: 0 }} />
                  </tr>
                )}
              </>
            )}
          </tbody>
        </table>
      </div>

      {footer}
    </section>
  )
}
