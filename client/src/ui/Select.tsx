import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { Icon } from './Icon'
import { Loader } from './Loader'
import { useDebouncedValue } from './hooks/useDebouncedValue'
import styles from './Select.module.css'

export interface SelectOption<V extends string | number = string | number> {
  value: V
  label: string
  /** Secondary text shown under the label (SKU, phone number …). */
  hint?: string
  disabled?: boolean
}

interface SelectProps<V extends string | number> {
  /** The chosen option (full object, so async lists can show a label the current page no longer contains). */
  value: SelectOption<V> | null
  onChange: (option: SelectOption<V> | null) => void
  /** Static options — filtered client-side as the user types. */
  options?: SelectOption<V>[]
  /** Async options (replaces select2's ajax): called with the typed text, debounced. Wins over `options`. */
  loadOptions?: (query: string) => Promise<SelectOption<V>[]>
  placeholder?: string
  clearable?: boolean
  disabled?: boolean
  invalid?: boolean
  id?: string
  name?: string
  /** Called when the control loses focus (react-hook-form `onBlur`). */
  onBlur?: () => void
  'aria-label'?: string
  'aria-describedby'?: string
  'aria-required'?: boolean
}

/**
 * Searchable single-select (ARIA 1.2 combobox with a listbox popup). The popup is portalled and
 * positioned against the input so it is never clipped by a scrolling modal body or table.
 * Keyboard: ↓/↑ move, Enter picks, Esc closes, typing filters (or queries `loadOptions`).
 */
export function Select<V extends string | number = string | number>({
  value,
  onChange,
  options,
  loadOptions,
  placeholder,
  clearable = false,
  disabled,
  invalid,
  id,
  name,
  onBlur,
  'aria-label': ariaLabel,
  'aria-describedby': describedBy,
  'aria-required': required,
}: SelectProps<V>) {
  const { t } = useTranslation()
  const autoId = useId()
  const inputId = id ?? autoId
  const listId = `${inputId}-list`
  const inputRef = useRef<HTMLInputElement>(null)
  const wrapRef = useRef<HTMLDivElement>(null)
  const listRef = useRef<HTMLUListElement>(null)

  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [rect, setRect] = useState<{ left: number; top: number; width: number; maxHeight: number } | null>(null)
  const [remote, setRemote] = useState<{ query: string; items: SelectOption<V>[]; failed: boolean } | null>(null)

  const debouncedQuery = useDebouncedValue(query, loadOptions ? 250 : 0)

  // Async loading: one request per settled query while the popup is open; stale responses are dropped.
  useEffect(() => {
    if (!open || !loadOptions) return
    let cancelled = false
    loadOptions(debouncedQuery)
      .then((items) => {
        if (!cancelled) setRemote({ query: debouncedQuery, items, failed: false })
      })
      .catch(() => {
        if (!cancelled) setRemote({ query: debouncedQuery, items: [], failed: true })
      })
    return () => {
      cancelled = true
    }
  }, [open, debouncedQuery, loadOptions])

  // Loading = the latest typed text hasn't been answered yet (covers the debounce gap too).
  const loading = !!loadOptions && open && (query !== debouncedQuery || remote?.query !== debouncedQuery)
  const failed = !loading && !!remote?.failed

  const items = useMemo<SelectOption<V>[]>(() => {
    if (loadOptions) return remote?.items ?? []
    const q = query.trim().toLowerCase()
    const all = options ?? []
    return q ? all.filter((o) => `${o.label} ${o.hint ?? ''}`.toLowerCase().includes(q)) : all
  }, [loadOptions, remote, options, query])

  const place = useCallback(() => {
    const el = wrapRef.current
    if (!el) return
    const r = el.getBoundingClientRect()
    const below = window.innerHeight - r.bottom - 12
    const above = r.top - 12
    // Flip upward only when there is clearly more room above.
    const flip = below < 200 && above > below
    setRect({
      left: r.left,
      width: r.width,
      top: flip ? Math.max(8, r.top - Math.min(280, above) - 4) : r.bottom + 4,
      maxHeight: Math.min(280, flip ? above : below),
    })
  }, [])

  useLayoutEffect(() => {
    if (!open) return
    place()
    window.addEventListener('resize', place)
    window.addEventListener('scroll', place, true)
    return () => {
      window.removeEventListener('resize', place)
      window.removeEventListener('scroll', place, true)
    }
  }, [open, place])

  // Click outside closes. Mousedown on the list is preventDefault'ed so the input keeps focus.
  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      const target = e.target as Node
      if (!wrapRef.current?.contains(target) && !listRef.current?.contains(target)) close()
    }
    document.addEventListener('mousedown', onDown)
    return () => document.removeEventListener('mousedown', onDown)
  }, [open])

  useEffect(() => {
    listRef.current?.children[active]?.scrollIntoView?.({ block: 'nearest' })
  }, [active, open])

  function close() {
    setOpen(false)
    setQuery('')
    setActive(0)
  }

  const pick = (opt: SelectOption<V>) => {
    if (opt.disabled) return
    onChange(opt)
    close()
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (disabled) return
    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
      e.preventDefault()
      if (!open) {
        setOpen(true)
        return
      }
      if (items.length === 0) return
      setActive((a) => (e.key === 'ArrowDown' ? (a + 1) % items.length : (a - 1 + items.length) % items.length))
    } else if (e.key === 'Enter' && open) {
      e.preventDefault()
      const opt = items[active]
      if (opt) pick(opt)
    } else if (e.key === 'Escape' && open) {
      // Mark handled so an enclosing Modal doesn't also close.
      e.preventDefault()
      close()
    } else if (e.key === 'Tab') {
      close()
    }
  }

  const activeId = open && items[active] ? `${listId}-${active}` : undefined
  const shown = open ? query : (value?.label ?? '')
  const showClear = clearable && value && !disabled

  return (
    <div className={styles.wrap} ref={wrapRef} data-invalid={invalid || undefined} data-disabled={disabled || undefined}>
      <input
        ref={inputRef}
        id={inputId}
        name={name}
        className={styles.input}
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-activedescendant={activeId}
        aria-autocomplete="list"
        aria-label={ariaLabel}
        aria-describedby={describedBy}
        aria-invalid={invalid || undefined}
        aria-required={required}
        autoComplete="off"
        disabled={disabled}
        placeholder={open && value ? value.label : placeholder}
        value={shown}
        onChange={(e) => {
          setQuery(e.target.value)
          setActive(0)
          if (!open) setOpen(true)
        }}
        onFocus={() => !disabled && setOpen(true)}
        onClick={() => !disabled && setOpen(true)}
        onBlur={onBlur}
        onKeyDown={onKeyDown}
      />
      <span className={styles.adornments}>
        {loading && <Loader size="sm" tone="muted" />}
        {showClear && (
          <button
            type="button"
            className={styles.clear}
            tabIndex={-1}
            aria-label={t('common.clearSelection', 'Clear selection')}
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => {
              onChange(null)
              inputRef.current?.focus()
            }}
          >
            <Icon name="close" size={14} />
          </button>
        )}
        <span className={styles.chevron} aria-hidden="true">
          <Icon name="chevron-down" size={16} />
        </span>
      </span>

      {open &&
        rect &&
        createPortal(
          <ul
            ref={listRef}
            id={listId}
            role="listbox"
            className={styles.list}
            // Fixed viewport coordinates from getBoundingClientRect are physical, correct in both directions.
            style={{ left: rect.left, top: rect.top, inlineSize: rect.width, maxBlockSize: rect.maxHeight }}
            onMouseDown={(e) => e.preventDefault()}
          >
            {items.map((opt, i) => (
              <li
                key={String(opt.value)}
                id={`${listId}-${i}`}
                role="option"
                aria-selected={value?.value === opt.value}
                aria-disabled={opt.disabled || undefined}
                className={styles.option}
                data-active={i === active || undefined}
                data-selected={value?.value === opt.value || undefined}
                onMouseEnter={() => setActive(i)}
                onClick={() => pick(opt)}
              >
                <span className={styles.optionLabel}><bdi>{opt.label}</bdi></span>
                {opt.hint && <span className={styles.optionHint}><bdi>{opt.hint}</bdi></span>}
              </li>
            ))}
            {items.length === 0 && (
              <li className={styles.status} role="presentation">
                {failed
                  ? t('common.loadFailed', 'Could not load options')
                  : loading
                    ? t('common.loading', 'Loading')
                    : t('common.noMatches', 'No matches')}
              </li>
            )}
          </ul>,
          document.body,
        )}
    </div>
  )
}
