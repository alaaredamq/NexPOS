import { useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { create } from 'zustand'
import { IconButton } from '@/ui/IconButton'
import type { FeedItem } from '../api'
import { useItemCopy } from '../copy'
import { readSeen, writeSeen } from './seenToasts'
import styles from './bell.module.css'

const TOAST_MS = 6000
const MAX_PER_POLL = 3

// Held outside React (like ui/toast): the poll hands items over from an effect, and the stack just renders what is queued.
const useQueue = create<{ toasts: FeedItem[] }>(() => ({ toasts: [] }))
/** Empties the stack (tests; the stack otherwise outlives the bell, so a toast survives a re-render of the shell). */
export const resetBellToasts = () => useQueue.setState({ toasts: [] })
const dismissToast = (key: string) => useQueue.setState((state) => ({ toasts: state.toasts.filter((toast) => toast.key !== key) }))

/**
 * Pops a toast, once, for each unread item the bell has not shown before (the first visit can have many, so a burst is capped).
 * Seen keys are remembered in localStorage. While the dropdown is open the list itself is the notification, so nothing pops.
 */
export function useFreshToasts(items: FeedItem[] | undefined, suppressed: boolean): [FeedItem[], (key: string) => void] {
  const toasts = useQueue((state) => state.toasts)
  const handled = useRef<FeedItem[] | undefined>(undefined)

  useEffect(() => {
    if (!items || handled.current === items) return
    handled.current = items
    const seen = readSeen()
    const fresh = items.filter((item) => !item.isRead && !seen.includes(item.key))
    if (fresh.length === 0) return

    writeSeen([...seen, ...fresh.map((item) => item.key)])
    // Oldest first, so the newest ends up at the bottom of the stack.
    if (!suppressed) useQueue.setState((state) => ({ toasts: [...state.toasts, ...fresh.slice(0, MAX_PER_POLL).reverse()] }))
  }, [items, suppressed])

  useEffect(() => {
    if (suppressed) useQueue.setState({ toasts: [] })
  }, [suppressed])

  return [toasts, dismissToast]
}

interface BellToastsProps {
  toasts: FeedItem[]
  onDismiss: (key: string) => void
  onOpen: (item: FeedItem) => void
}

export function BellToasts({ toasts, onDismiss, onOpen }: BellToastsProps) {
  if (toasts.length === 0) return null
  return createPortal(
    <div className={styles.toastStack} aria-live="polite" aria-relevant="additions">
      {toasts.map((item) => (
        <Toast key={item.key} item={item} onDismiss={onDismiss} onOpen={onOpen} />
      ))}
    </div>,
    document.body,
  )
}

function Toast({ item, onDismiss, onOpen }: { item: FeedItem } & Omit<BellToastsProps, 'toasts'>) {
  const { t } = useTranslation('notifications', { useSuspense: false })
  const copy = useItemCopy()

  useEffect(() => {
    const timer = window.setTimeout(() => onDismiss(item.key), TOAST_MS)
    return () => window.clearTimeout(timer)
  }, [item.key, onDismiss])

  return (
    <div className={styles.toast} data-kind={item.kind} role="status">
      <span className={styles.dot} aria-hidden="true" />
      <button
        type="button"
        className={styles.toastBody}
        onClick={() => {
          onDismiss(item.key)
          onOpen(item)
        }}
      >
        <span className={styles.itemTitle}>{copy.title(item)}</span>
        <span className={styles.itemMessage}>{copy.message(item)}</span>
      </button>
      <IconButton icon="close" size="sm" label={t('dismiss')} onClick={() => onDismiss(item.key)} />
    </div>
  )
}
