import type { PosCommand } from '../slots'

export type ShortcutAction =
  | { type: 'focusSearch' }
  | { type: 'priceCheck' }
  | { type: 'command'; command: PosCommand }
  /** Jump to a management page by its nav key (the old Alt+D / Alt+I / Alt+O / Alt+P). */
  | { type: 'navigate'; navKey: string }

export interface ShortcutInfo {
  /** Display label for the key combination. */
  keys: string
  /** Key into the `pos` namespace describing the action. */
  labelKey: string
}

const FUNCTION_KEYS: Record<string, ShortcutAction> = {
  F2: { type: 'focusSearch' },
  F3: { type: 'priceCheck' },
  F4: { type: 'command', command: 'retrieve' },
  F6: { type: 'command', command: 'hold' },
  F8: { type: 'command', command: 'discount' },
  F9: { type: 'command', command: 'clear' },
}

const ALT_KEYS: Record<string, string> = { d: 'Dashboard', i: 'Inventory', o: 'Orders', p: 'Products' }

/**
 * What a key press means at the till, or null. Function keys are used on purpose: letters belong to the scan box, and F5 / F11 / F12
 * (reload, full screen, dev tools) are left alone. Alt+D/I/O/P are the old navigation shortcuts.
 */
export function shortcutFor(e: Pick<KeyboardEvent, 'key' | 'ctrlKey' | 'altKey' | 'metaKey' | 'shiftKey'>): ShortcutAction | null {
  if (e.metaKey) return null
  if (e.ctrlKey && !e.altKey && !e.shiftKey && e.key === 'Enter') return { type: 'command', command: 'checkout' }
  if (e.altKey && !e.ctrlKey && !e.shiftKey) {
    const navKey = ALT_KEYS[e.key.toLowerCase()]
    return navKey ? { type: 'navigate', navKey } : null
  }
  if (e.ctrlKey || e.altKey || e.shiftKey) return null
  return FUNCTION_KEYS[e.key] ?? null
}

/** The list the help dialog shows, in the order a cashier meets them. */
export const SHORTCUT_HELP: readonly ShortcutInfo[] = [
  { keys: 'F2', labelKey: 'shortcutSearch' },
  { keys: 'F3', labelKey: 'PriceCheck' },
  { keys: 'F4', labelKey: 'RetrieveHeldCart' },
  { keys: 'F6', labelKey: 'holdCart' },
  { keys: 'F8', labelKey: 'ApplyDiscount' },
  { keys: 'F9', labelKey: 'clearCart' },
  { keys: 'Ctrl + Enter', labelKey: 'Checkout' },
  { keys: 'Esc', labelKey: 'shortcutEscape' },
  { keys: 'Alt + D / I / O / P', labelKey: 'shortcutNavigate' },
]
