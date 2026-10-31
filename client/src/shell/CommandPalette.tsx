import { useEffect, useMemo, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { NavIcon } from './NavIcon'
import { SEARCH_ICON, navIconPath } from './navIcons'
import { flattenNav } from './nav'
import { useShellStore } from './shellStore'
import { useOpenNavItem } from './useOpenNavItem'
import styles from './CommandPalette.module.css'

/**
 * Ctrl/Cmd+K jump-to-page. Lists only pages the signed-in user may open (`session.nav` is already filtered by the server),
 * so it can never become a directory of things they cannot reach. Matches on the translated label; prefix matches first.
 */
export function CommandPalette() {
  const open = useShellStore((s) => s.paletteOpen)
  const setOpen = useShellStore((s) => s.setPaletteOpen)

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        setOpen(!useShellStore.getState().paletteOpen)
      } else if (e.key === 'Escape' && useShellStore.getState().paletteOpen) {
        setOpen(false)
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [setOpen])

  return open ? <PaletteDialog /> : null
}

function PaletteDialog() {
  const { t } = useTranslation()
  const { nav } = useSession()
  const setOpen = useShellStore((s) => s.setPaletteOpen)
  const openItem = useOpenNavItem()

  const [query, setQuery] = useState('')
  const [cursorRaw, setCursor] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)

  const results = useMemo(() => {
    const items = flattenNav(nav)
    const q = query.trim().toLowerCase()
    if (!q) return items
    return items
      .filter((item) => item.label.toLowerCase().includes(q))
      .sort((a, b) => Number(b.label.toLowerCase().startsWith(q)) - Number(a.label.toLowerCase().startsWith(q)))
  }, [nav, query])

  // The highlight stays inside the list as it shrinks under a narrowing query.
  const cursor = Math.min(cursorRaw, Math.max(results.length - 1, 0))

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  const go = (index: number) => {
    const item = results[index]
    if (!item) return
    setOpen(false)
    openItem(item)
  }

  const onInputKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setCursor(results.length === 0 ? 0 : (cursor + 1) % results.length)
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setCursor(results.length === 0 ? 0 : (cursor - 1 + results.length) % results.length)
    } else if (e.key === 'Enter') {
      e.preventDefault()
      go(cursor)
    }
  }

  return (
    <div className={styles.overlay} onMouseDown={() => setOpen(false)}>
      <div className={styles.palette} role="dialog" aria-modal="true" aria-label={t('JumpTo')} onMouseDown={(e) => e.stopPropagation()}>
        <div className={styles.searchRow}>
          <span className={styles.searchIcon}>
            <NavIcon d={SEARCH_ICON} size={17} />
          </span>
          <input
            ref={inputRef}
            className={styles.input}
            value={query}
            placeholder={t('JumpToPlaceholder')}
            aria-label={t('JumpTo')}
            onChange={(e) => {
              setQuery(e.target.value)
              setCursor(0)
            }}
            onKeyDown={onInputKeyDown}
          />
          <kbd className={styles.kbd}>esc</kbd>
        </div>

        <div className={styles.results}>
          {results.length === 0 ? (
            <div className={styles.empty}>{t('JumpToNoResults')}</div>
          ) : (
            results.map((item, i) => (
              <button
                key={item.key}
                type="button"
                className={styles.result}
                data-active={i === cursor}
                onMouseEnter={() => setCursor(i)}
                onClick={() => go(i)}
              >
                <span className={styles.resultIcon}>
                  <NavIcon d={navIconPath(item.icon)} size={16} />
                </span>
                <span className={styles.resultLabel}>{item.label}</span>
                <span className={styles.resultGroup}>{item.sectionLabel}</span>
              </button>
            ))
          )}
        </div>
      </div>
    </div>
  )
}
