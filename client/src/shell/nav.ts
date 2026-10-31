import type { NavItem, NavSection, Session } from '@/api/types'

/** The router `basename` (vite `base`); `session.nav[].spaPath` is an absolute URL path including it. */
export const SPA_BASE = '/app'

/** `/app/cash-drawer` -> `/cash-drawer`: the path as the router and `<Link>` want it. */
export function toRouterPath(spaPath: string): string {
  if (spaPath === SPA_BASE) return '/'
  return spaPath.startsWith(`${SPA_BASE}/`) ? spaPath.slice(SPA_BASE.length) : spaPath
}

export interface FlatNavItem extends NavItem {
  sectionKey: string
  sectionLabel: string
  /** `spaPath` as a router path. */
  routerPath: string
}

export function flattenNav(sections: readonly NavSection[]): FlatNavItem[] {
  return sections.flatMap((section) =>
    section.items.map((item) => ({
      ...item,
      sectionKey: section.key,
      sectionLabel: section.label,
      routerPath: toRouterPath(item.spaPath),
    })),
  )
}

/** Same segment-boundary rule as the old rail: `/pos` must not light up `/pos-display`. */
export function isActivePath(pathname: string, routerPath: string): boolean {
  return pathname === routerPath || pathname.startsWith(`${routerPath}/`)
}

/** The nav entry the current URL belongs to; the longest match wins. */
export function findCurrent(items: readonly FlatNavItem[], pathname: string): FlatNavItem | undefined {
  return items
    .filter((item) => isActivePath(pathname, item.routerPath))
    .sort((a, b) => b.routerPath.length - a.routerPath.length)[0]
}

/**
 * Where a user lands after signing in: the point of sale for whoever may open it (the Razor app's home), otherwise the first
 * page their role grants. Null for a role that grants no page at all - the shell shows "nothing assigned" instead of looping.
 */
export function landingPath(session: Session): string | null {
  const items = flattenNav(session.nav)
  const pos = items.find((item) => item.key === 'POS')
  return (pos ?? items[0])?.routerPath ?? null
}

/** A Razor page of the old app, in the user's current language (used for pages with no SPA version yet). */
export function legacyAccountPath(language: string, page: 'ChangePin' | 'Profile'): string {
  return `/${language}/Account/${page}`
}
