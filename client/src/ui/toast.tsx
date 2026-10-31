import { useEffect } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { create } from 'zustand'
import { Icon, type IconName } from './Icon'
import { IconButton } from './IconButton'
import styles from './toast.module.css'

export type ToastTone = 'success' | 'error' | 'warning' | 'info'

interface ToastItem {
  id: number
  tone: ToastTone
  message: string
  /** ms before auto-dismiss; 0 keeps it until closed. */
  duration: number
}

const useToastStore = create<{ items: ToastItem[] }>(() => ({ items: [] }))

let nextId = 1
const MAX_VISIBLE = 4
const DEFAULT_DURATION: Record<ToastTone, number> = { success: 3000, info: 4000, warning: 5000, error: 6000 }

function push(tone: ToastTone, message: string, duration?: number): number {
  const id = nextId++
  useToastStore.setState((s) => ({
    items: [...s.items, { id, tone, message, duration: duration ?? DEFAULT_DURATION[tone] }].slice(-MAX_VISIBLE),
  }))
  return id
}

function dismiss(id: number) {
  useToastStore.setState((s) => ({ items: s.items.filter((i) => i.id !== id) }))
}

/**
 * Replacement for toastr (~163 call sites): `toast.success('Saved')`, `toast.error(problem.detail)`.
 * Plain functions on purpose — the http client's error handler calls these outside React.
 * Requires `<Toaster />` to be mounted once (see UiHost).
 */
export const toast = {
  success: (message: string, duration?: number) => push('success', message, duration),
  error: (message: string, duration?: number) => push('error', message, duration),
  warning: (message: string, duration?: number) => push('warning', message, duration),
  info: (message: string, duration?: number) => push('info', message, duration),
  dismiss,
  clear: () => useToastStore.setState({ items: [] }),
}

const TONE_ICON: Record<ToastTone, IconName> = { success: 'check', error: 'error', warning: 'warning', info: 'info' }

function ToastView({ item }: { item: ToastItem }) {
  const { t } = useTranslation()
  useEffect(() => {
    if (item.duration <= 0) return
    const timer = window.setTimeout(() => dismiss(item.id), item.duration)
    return () => window.clearTimeout(timer)
  }, [item.id, item.duration])

  return (
    // Errors interrupt (role=alert, assertive); everything else is polite.
    <div className={styles.toast} data-tone={item.tone} role={item.tone === 'error' ? 'alert' : 'status'}>
      <span className={styles.icon}>
        <Icon name={TONE_ICON[item.tone]} size={18} />
      </span>
      <span className={styles.message}>{item.message}</span>
      <IconButton icon="close" size="sm" label={t('common.dismiss', 'Dismiss')} onClick={() => dismiss(item.id)} />
    </div>
  )
}

export function Toaster() {
  const items = useToastStore((s) => s.items)
  return createPortal(
    <div className={styles.region} aria-live="polite" aria-relevant="additions">
      {items.map((item) => (
        <ToastView key={item.id} item={item} />
      ))}
    </div>,
    document.body,
  )
}
