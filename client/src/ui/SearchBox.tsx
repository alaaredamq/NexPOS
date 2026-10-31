import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from './Icon'
import styles from './SearchBox.module.css'

interface SearchBoxProps {
  /** The committed query (what the list is currently filtered by). */
  value: string
  /** Fires `delay` ms after typing stops, immediately on Enter, and immediately on clear. */
  onSearch: (query: string) => void
  placeholder?: string
  delay?: number
  autoFocus?: boolean
  'aria-label'?: string
}

/** Debounced list filter. Keeps its own draft text so typing never waits on the server round-trip. */
export function SearchBox({ value, onSearch, placeholder, delay = 300, autoFocus, 'aria-label': ariaLabel }: SearchBoxProps) {
  const { t } = useTranslation()
  const [draft, setDraft] = useState(value)
  const lastCommitted = useRef(value)
  const onSearchRef = useRef(onSearch)
  useEffect(() => {
    onSearchRef.current = onSearch
  })

  // The parent changed the query (e.g. "reset filters") — follow it.
  useEffect(() => {
    if (value !== lastCommitted.current) {
      lastCommitted.current = value
      setDraft(value)
    }
  }, [value])

  useEffect(() => {
    if (draft === lastCommitted.current) return
    const timer = window.setTimeout(() => {
      lastCommitted.current = draft
      onSearchRef.current(draft)
    }, delay)
    return () => window.clearTimeout(timer)
  }, [draft, delay])

  const commit = (q: string) => {
    lastCommitted.current = q
    onSearch(q)
  }

  const label = ariaLabel ?? placeholder ?? t('common.search', 'Search')

  return (
    <div className={styles.wrap} role="search">
      <span className={styles.icon}>
        <Icon name="search" size={16} />
      </span>
      <input
        className={styles.input}
        type="search"
        value={draft}
        placeholder={placeholder ?? t('common.search', 'Search')}
        aria-label={label}
        autoFocus={autoFocus}
        autoComplete="off"
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter') commit(draft)
          else if (e.key === 'Escape' && draft) {
            e.preventDefault()
            e.stopPropagation()
            setDraft('')
            commit('')
          }
        }}
      />
      {draft && (
        <button
          type="button"
          className={styles.clear}
          aria-label={t('common.clearSearch', 'Clear search')}
          onClick={() => {
            setDraft('')
            commit('')
          }}
        >
          <Icon name="close" size={14} />
        </button>
      )}
    </div>
  )
}
