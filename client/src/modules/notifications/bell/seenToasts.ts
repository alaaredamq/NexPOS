/**
 * Which items already popped a toast, remembered across refreshes and tabs. Same key as the Razor top bar's script, so a user who
 * moves between the old pages and the SPA is not shown the same alert twice.
 */
const STORAGE_KEY = 'nexpos-notification-toasts-seen'
const MAX_KEYS = 200

export function readSeen(): string[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    const parsed: unknown = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed) ? parsed.filter((key): key is string => typeof key === 'string') : []
  } catch {
    return []
  }
}

export function writeSeen(keys: string[]) {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(keys.slice(-MAX_KEYS)))
  } catch {
    // Private mode / quota: toasts may repeat after a refresh, nothing else breaks.
  }
}
