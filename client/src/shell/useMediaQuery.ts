import { useSyncExternalStore } from 'react'

/** Below this width the rail is an off-canvas drawer (same breakpoint as the Razor sidebar). */
export const DRAWER_QUERY = '(max-width: 991px)'

export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (notify) => {
      const list = window.matchMedia(query)
      list.addEventListener('change', notify)
      return () => list.removeEventListener('change', notify)
    },
    () => window.matchMedia(query).matches,
    () => false,
  )
}
