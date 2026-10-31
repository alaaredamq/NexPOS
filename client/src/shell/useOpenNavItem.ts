import { useCallback } from 'react'
import { useNavigate } from 'react-router-dom'
import type { FlatNavItem } from './nav'

/** Open a rail/palette entry: in-app navigation for a migrated page, a full page load (same cookie) for a Razor one. */
export function useOpenNavItem() {
  const navigate = useNavigate()
  return useCallback(
    (item: FlatNavItem) => {
      if (item.migrated) void navigate(item.routerPath)
      else window.location.assign(item.href)
    },
    [navigate],
  )
}
