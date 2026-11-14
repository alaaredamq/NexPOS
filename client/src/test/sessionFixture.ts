import type { NavItem, NavSection, Session } from '@/api/types'

export function navItem(key: string, overrides: Partial<NavItem> = {}): NavItem {
  const kebab = key.replace(/([a-z])([A-Z])/g, '$1-$2').toLowerCase()
  return {
    key,
    labelKey: key,
    label: key,
    icon: 'fa-box',
    permissions: [],
    alsoActiveOn: [],
    spaPath: `/app/${kebab}`,
    legacyPath: `/en/${key}`,
    migrated: false,
    href: `/en/${key}`,
    ...overrides,
  }
}

export function section(key: string, items: NavItem[]): NavSection {
  return { key, labelKey: key, label: key, items }
}

/** A cashier-sized session: POS, Orders (migrated) and Notifications. Override pieces per test. */
export function makeSession(overrides: Partial<Session> = {}): Session {
  return {
    user: { id: 2, username: 'sara', fullName: 'Sara Cashier', initials: 'SC', mustChangePin: false },
    role: { id: 2, name: 'Cashier' },
    permissions: ['Orders.View'],
    nav: [
      section('Sales', [
        navItem('POS', { label: 'Point of Sale' }),
        navItem('Orders', { label: 'Orders', migrated: true, href: '/app/orders' }),
      ]),
      section('Insights', [navItem('Notifications', { label: 'Notifications' })]),
    ],
    license: {
      status: 'Active',
      allowsSales: true,
      showsWarning: false,
      enforcementDisabled: false,
      title: 'Licensed',
      message: 'Coduo POS is activated on this computer.',
      severity: 'success',
    },
    branding: { appName: 'Coduo POS', hasCustomLogo: false, logoUrl: '/branding/logo', markUrl: '/branding/logo?variant=mark', faviconUrl: '/branding/favicon' },
    language: 'en',
    direction: 'ltr',
    drawer: { isOpen: true, openingCash: 100, sessionId: 1 },
    unreadNotifications: 3,
    ...overrides,
  }
}

export function problem(status: number, code: string, title = 'Problem') {
  return new Response(JSON.stringify({ status, code, title, traceId: 't' }), {
    status,
    headers: { 'Content-Type': 'application/problem+json' },
  })
}

export function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
