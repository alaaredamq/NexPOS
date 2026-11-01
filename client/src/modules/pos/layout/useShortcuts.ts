import { useEffect } from 'react'
import { useSession } from '@/core/auth/useSession'
import { flattenNav } from '@/shell/nav'
import { useOpenNavItem } from '@/shell/useOpenNavItem'
import { focusSearch } from '../search/focusSearch'
import { dialogIsOpen } from '../scan/useScanFocus'
import { posBus } from '../slots'
import { shortcutFor } from './shortcuts'

/** Wires `shortcutFor` to the page. Nothing fires while a dialog is open: the dialog owns the keyboard then. */
export function useShortcuts(openPriceCheck: () => void): void {
  const { nav } = useSession()
  const open = useOpenNavItem()

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.defaultPrevented || e.repeat || dialogIsOpen()) return
      const action = shortcutFor(e)
      if (!action) return
      e.preventDefault()

      switch (action.type) {
        case 'focusSearch':
          focusSearch()
          break
        case 'priceCheck':
          openPriceCheck()
          break
        case 'command':
          posBus.run(action.command)
          break
        case 'navigate': {
          const target = flattenNav(nav).find((item) => item.key === action.navKey)
          if (target) open(target)
          break
        }
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [nav, open, openPriceCheck])
}
