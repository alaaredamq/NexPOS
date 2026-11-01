/** F2 (see layout/useShortcuts) asks the search box to take focus. A two-line channel so the shortcut and the box need not know each other. */
const listeners = new Set<() => void>()

export const focusSearch = () => listeners.forEach((listener) => listener())

export function onFocusSearch(listener: () => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}
