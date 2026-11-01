import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import type { FlatNavItem } from '@/shell/nav'

interface NavTargetProps {
  item: FlatNavItem
  /** Appended to the page path: `${path}/${suffix}`. The Razor page uses `Details/${id}`, the SPA `${id}`. */
  spaSuffix?: string
  legacySuffix?: string
  className?: string
  children: ReactNode
}

/** A link to a rail page: in-app for a migrated one, a full page load (same cookie) for a Razor one that is still live. */
export function NavTarget({ item, spaSuffix, legacySuffix, className, children }: NavTargetProps) {
  return item.migrated ? (
    <Link className={className} to={spaSuffix ? `${item.routerPath}/${spaSuffix}` : item.routerPath}>
      {children}
    </Link>
  ) : (
    <a className={className} href={legacySuffix ? `${item.href}/${legacySuffix}` : item.href}>
      {children}
    </a>
  )
}
