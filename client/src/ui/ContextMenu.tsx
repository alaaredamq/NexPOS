import { useEffect, useLayoutEffect, useRef, useState, type MouseEvent as ReactMouseEvent } from 'react'
import { createPortal } from 'react-dom'
import { create } from 'zustand'
import styles from './ContextMenu.module.css'

export interface ContextMenuAction {
  key: string
  label: string
  onSelect: () => void
  disabled?: boolean
  /** Styles the item as a destructive action (e.g. void, remove, delete). */
  danger?: boolean
}

export type ContextMenuEntry = ContextMenuAction | { type: 'separator'; key: string }

interface MenuState {
  x: number
  y: number
  entries: ContextMenuEntry[]
}

const useMenuStore = create<{ menu: MenuState | null }>(() => ({ menu: null }))

/**
 * Opens the shared context menu at the event's position. An empty `entries` array is a no-op (the
 * native menu is still suppressed). Usage: `<tr onContextMenu={(e) => openContextMenu(e, entries)}>`.
 */
export function openContextMenu(event: ReactMouseEvent, entries: ContextMenuEntry[]) {
  event.preventDefault()
  event.stopPropagation()
  if (entries.length === 0) return
  useMenuStore.setState({ menu: { x: event.clientX, y: event.clientY, entries } })
}

/** Hook form of {@link openContextMenu}, for call sites that prefer it. */
export function useContextMenu() {
  return openContextMenu
}

const close = () => useMenuStore.setState({ menu: null })

/** Mounted once (UiHost). Also suppresses the browser's native menu on right-clicks outside editable fields. */
export function ContextMenuHost() {
  const menu = useMenuStore((s) => s.menu)

  useEffect(() => {
    const suppress = (e: MouseEvent) => {
      const el = e.target as HTMLElement | null
      // Keep the native menu where people paste or spell-check.
      if (el?.closest('input, textarea, [contenteditable="true"]')) return
      e.preventDefault()
    }
    document.addEventListener('contextmenu', suppress)
    return () => document.removeEventListener('contextmenu', suppress)
  }, [])

  return menu ? <ContextMenuSurface x={menu.x} y={menu.y} entries={menu.entries} /> : null
}

function ContextMenuSurface({ x, y, entries }: MenuState) {
  const ref = useRef<HTMLDivElement | null>(null)
  const [pos, setPos] = useState({ x, y, ready: false })

  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    setPos({
      x: Math.max(8, Math.min(x, window.innerWidth - rect.width - 8)),
      y: Math.max(8, Math.min(y, window.innerHeight - rect.height - 8)),
      ready: true,
    })
  }, [x, y, entries])

  useEffect(() => {
    ref.current?.querySelector<HTMLElement>('[role="menuitem"]:not(:disabled)')?.focus()
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault()
        close()
      } else if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
        e.preventDefault()
        const items = Array.from(ref.current?.querySelectorAll<HTMLElement>('[role="menuitem"]:not(:disabled)') ?? [])
        if (items.length === 0) return
        const i = items.indexOf(document.activeElement as HTMLElement)
        const next = e.key === 'ArrowDown' ? (i + 1) % items.length : (i - 1 + items.length) % items.length
        items[next]?.focus()
      }
    }
    window.addEventListener('keydown', handleKey)
    window.addEventListener('scroll', close, true)
    window.addEventListener('resize', close)
    return () => {
      window.removeEventListener('keydown', handleKey)
      window.removeEventListener('scroll', close, true)
      window.removeEventListener('resize', close)
    }
  }, [])

  return createPortal(
    <>
      <div
        className={styles.backdrop}
        onMouseDown={close}
        onContextMenu={(e) => {
          e.preventDefault()
          close()
        }}
      />
      <div
        ref={ref}
        className={styles.menu}
        role="menu"
        // Viewport coordinates from the pointer event are physical by nature, so left/top is correct in RTL too.
        style={{ left: pos.x, top: pos.y, visibility: pos.ready ? 'visible' : 'hidden' }}
      >
        {entries.map((entry) =>
          'type' in entry ? (
            <div key={entry.key} className={styles.separator} role="separator" />
          ) : (
            <button
              key={entry.key}
              type="button"
              role="menuitem"
              className={styles.item}
              data-danger={entry.danger || undefined}
              disabled={entry.disabled}
              onClick={() => {
                close()
                entry.onSelect()
              }}
            >
              {entry.label}
            </button>
          ),
        )}
      </div>
    </>,
    document.body,
  )
}
