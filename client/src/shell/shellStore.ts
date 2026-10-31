import { create } from 'zustand'

/** Same key and values as the Razor rail (`site.js`), so the choice carries over while both UIs exist. */
const RAIL_KEY = 'nexpos.rail'

function readCollapsed(): boolean {
  try {
    return localStorage.getItem(RAIL_KEY) === 'collapsed'
  } catch {
    return false
  }
}

interface ShellState {
  /** Desktop widths: rail shows icons only. */
  railCollapsed: boolean
  /** Below 992 px the rail is an off-canvas drawer. */
  drawerOpen: boolean
  paletteOpen: boolean
  toggleRail: () => void
  setDrawerOpen: (open: boolean) => void
  setPaletteOpen: (open: boolean) => void
}

export const useShellStore = create<ShellState>((set, get) => ({
  railCollapsed: readCollapsed(),
  drawerOpen: false,
  paletteOpen: false,
  toggleRail: () => {
    const next = !get().railCollapsed
    try {
      localStorage.setItem(RAIL_KEY, next ? 'collapsed' : 'open')
    } catch {
      // private mode: stays for this page load only
    }
    set({ railCollapsed: next })
  },
  setDrawerOpen: (drawerOpen) => set({ drawerOpen }),
  setPaletteOpen: (paletteOpen) => set({ paletteOpen }),
}))
