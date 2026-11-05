import { ICON_PATHS } from '@/ui/Icon'

/**
 * `session.nav[].icon` is the Font Awesome class of the Razor rail; the SPA draws its own stroke icons on the same
 * 24-grid as `ui/Icon`, so the packs never mix. Unknown classes fall back to a dot.
 */
const NAV_ICON_PATHS: Record<string, string> = {
  'fa-cash-register': 'M6 4h12v5H6z M4 9h16v11H4z M8 13h2 M12 13h2 M16 13h.01 M8 16.5h8',
  'fa-tachometer-alt': 'M4 18a8.5 8.5 0 1116 0 M12 14l4-5 M7 18h.01 M17 18h.01',
  'fa-shopping-cart': ICON_PATHS.cart,
  'fa-vault': 'M4 5h16v14H4z M12 9a3 3 0 100 6 3 3 0 000-6 M16.5 12h2 M7 19v1.5 M17 19v1.5',
  'fa-receipt': 'M6 3h12v18l-2-1.5-2 1.5-2-1.5-2 1.5-2-1.5L6 21z M9 8h6 M9 12h6',
  'fa-box': 'M4 8l8-4 8 4v8l-8 4-8-4z M4 8l8 4 8-4 M12 12v8',
  'fa-tags': 'M3 12V4h8l9 9-8 8z M7.5 8h.01',
  'fa-sort-amount-down': 'M4 6h10 M4 12h7 M4 18h4 M17 5v14 M14 16l3 3 3-3',
  'fa-warehouse': 'M3 10l9-6 9 6v10H3z M8 20v-6h8v6',
  'fa-file-invoice': 'M6 3h9l4 4v14H6z M15 3v4h4 M9 12h6 M9 16h6 M9 8h2',
  'fa-truck': 'M2 6h11v10H2z M13 9h4l3 3v4h-7 M6 19a1.5 1.5 0 100-3 1.5 1.5 0 000 3 M16 19a1.5 1.5 0 100-3 1.5 1.5 0 000 3',
  'fa-users': 'M9 11a3.5 3.5 0 100-7 3.5 3.5 0 000 7 M2.5 20a6.5 6.5 0 0113 0 M16 4.5a3.5 3.5 0 010 6.5 M18 14.5a6.5 6.5 0 013.5 5.5',
  'fa-chart-bar': 'M4 20V4 M4 20h16 M8 16v-5 M12 16V8 M16 16v-3',
  'fa-bell': 'M6 8a6 6 0 0112 0c0 7 3 9 3 9H3s3-2 3-9 M10.3 21a1.94 1.94 0 003.4 0',
  'fa-database': 'M4 6c0-1.5 3.6-3 8-3s8 1.5 8 3-3.6 3-8 3-8-1.5-8-3z M4 6v6c0 1.5 3.6 3 8 3s8-1.5 8-3V6 M4 12v6c0 1.5 3.6 3 8 3s8-1.5 8-3v-6',
  'fa-user-cog': 'M9 11a3.5 3.5 0 100-7 3.5 3.5 0 000 7 M2.5 20a6.5 6.5 0 0110-5 M18 19a2 2 0 100-4 2 2 0 000 4 M18 13v1.5 M18 19.5V21 M14.5 17h1.5 M20 17h1.5',
  'fa-coins': 'M9 11c3.3 0 6-1.3 6-3s-2.7-3-6-3-6 1.3-6 3 2.7 3 6 3z M3 8v4c0 1.7 2.7 3 6 3s6-1.3 6-3V8 M15.5 10.5c3 .2 5.5 1.4 5.5 3v4c0 1.7-2.7 3-6 3s-6-1.3-6-3',
  'fa-percentage': 'M19 5L5 19 M7 9a2 2 0 100-4 2 2 0 000 4 M17 19a2 2 0 100-4 2 2 0 000 4',
  'fa-cog':
    'M12 15a3 3 0 100-6 3 3 0 000 6z M19.4 15a1.7 1.7 0 00.3 1.8l.1.1a2 2 0 11-2.8 2.8l-.1-.1a1.7 1.7 0 00-1.8-.3 1.7 1.7 0 00-1 1.5V21a2 2 0 11-4 0v-.1a1.7 1.7 0 00-1.1-1.5 1.7 1.7 0 00-1.8.3l-.1.1a2 2 0 11-2.8-2.8l.1-.1a1.7 1.7 0 00.3-1.8 1.7 1.7 0 00-1.5-1H3a2 2 0 110-4h.1a1.7 1.7 0 001.5-1.1 1.7 1.7 0 00-.3-1.8l-.1-.1a2 2 0 112.8-2.8l.1.1a1.7 1.7 0 001.8.3H9a1.7 1.7 0 001-1.5V3a2 2 0 114 0v.1a1.7 1.7 0 001 1.5 1.7 1.7 0 001.8-.3l.1-.1a2 2 0 112.8 2.8l-.1.1a1.7 1.7 0 00-.3 1.8V9a1.7 1.7 0 001.5 1H21a2 2 0 110 4h-.1a1.7 1.7 0 00-1.5 1z',
  'fa-key': 'M15 8a4 4 0 11-3.9 4.9L3 21v-3h3v-3h3l2.1-2.1A4 4 0 0115 8z M16 9.5h.01',
  'fa-clipboard-list': 'M9 4h6v3H9z M7 5.5H5v15h14v-15h-2 M9 11h6 M9 15h6',
}

const FALLBACK = 'M12 13a1 1 0 100-2 1 1 0 000 2z'

export const MENU_ICON = 'M3 6h18 M3 12h18 M3 18h18'
export const BELL_ICON = NAV_ICON_PATHS['fa-bell']!
export const SEARCH_ICON = ICON_PATHS.search

export function navIconPath(faClass: string): string {
  return NAV_ICON_PATHS[faClass] ?? FALLBACK
}
