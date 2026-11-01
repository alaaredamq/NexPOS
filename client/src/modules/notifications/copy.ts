import { useCallback } from 'react'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { flattenNav } from '@/shell/nav'

/** What both the bell and the page need to word an alert or notification. */
export interface Wordable {
  kind: string
  title: string
  message: string
  params?: Record<string, unknown>
}

const UNFILLED = /\{\{\s*\w+\s*\}\}/

/**
 * Stock alerts are stored in English with their product facts as JSON; the UI words them from `kind` + `params` in the user's
 * language (`kinds.<kind>.title / .message`). Anything else - and a stock alert created by hand without the facts - shows the stored text
 * instead of a half-filled sentence.
 */
export function useItemCopy() {
  const { t, i18n } = useTranslation('notifications', { useSuspense: false })

  const word = useCallback(
    (key: string, item: Wordable, stored: string) => {
      if (!i18n.exists(key, { ns: 'notifications' })) return stored
      const text = t(key, { ...(item.params ?? {}), defaultValue: stored })
      return UNFILLED.test(text) ? stored : text
    },
    [t, i18n],
  )

  return {
    title: (item: Wordable) => word(`kinds.${item.kind}.title`, item, item.title),
    message: (item: Wordable) => word(`kinds.${item.kind}.message`, item, item.message),
  }
}

/** The server sends UTC; `Date` needs the `Z` to read it as such. */
const toTime = (iso: string) => new Date(/(Z|[+-]\d\d:?\d\d)$/.test(iso) ? iso : `${iso}Z`).getTime()

/** "Just now" / "5 min ago" / "3 h ago" / "2 d ago" (the bell's wording). */
export function useRelativeTime() {
  const { t } = useTranslation('notifications', { useSuspense: false })
  return (createdAt: string, now = Date.now()) => {
    const seconds = Math.max(0, Math.floor((now - toTime(createdAt)) / 1000))
    if (seconds < 60) return t('time.justNow')
    const minutes = Math.floor(seconds / 60)
    if (minutes < 60) return t('time.minutesAgo', { count: minutes })
    const hours = Math.floor(minutes / 60)
    if (hours < 24) return t('time.hoursAgo', { count: hours })
    return t('time.daysAgo', { count: Math.floor(hours / 24) })
  }
}

export interface ItemTarget {
  /** A page of the SPA (router path). */
  to?: string
  /** A page of the old app (full navigation). */
  href?: string
}

interface Linkable {
  productId?: number | null
}

/**
 * Where an item leads: the product a stock alert is about (when the user may open Products), otherwise the notifications page.
 * A page that has not moved to the SPA yet is a plain link to its Razor URL, as the nav does it.
 */
export function useItemTarget() {
  const nav = flattenNav(useSession().nav)
  const products = nav.find((item) => item.key === 'Products')
  const notifications = nav.find((item) => item.key === 'Notifications')

  return (item: Linkable): ItemTarget => {
    if (item.productId && products) {
      return products.migrated ? { to: `${products.routerPath}/${item.productId}` } : { href: `${products.href}/Details/${item.productId}` }
    }
    if (notifications) return notifications.migrated ? { to: notifications.routerPath } : { href: notifications.href }
    return {}
  }
}

/** The inventory page filtered to low stock, for the "view low stock items" link; null when the user has no Inventory page. */
export function useLowStockTarget(): ItemTarget | null {
  const inventory = flattenNav(useSession().nav).find((item) => item.key === 'Inventory')
  if (!inventory) return null
  return inventory.migrated ? { to: `${inventory.routerPath}/low-stock` } : { href: `${inventory.href}?filter=lowstock` }
}
