import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { resetBellToasts } from './bell/BellToasts'
import { readSeen } from './bell/seenToasts'
import routes from './routes'

const note = (id: number, title: string, overrides = {}) => ({
  id,
  kind: 'info',
  priority: 'normal',
  title,
  message: `${title} message`,
  isRead: false,
  createdAt: '2026-11-01T10:00:00Z',
  readAt: null,
  ...overrides,
})

const alertRow = (id: number, overrides = {}) => ({
  id,
  kind: 'lowStock',
  status: 'active',
  title: 'Low Stock Alert: Milk',
  message: 'Milk is running low',
  params: { productId: 9, productName: 'Milk', stockQuantity: 3, minimumStock: 10 },
  productId: 9,
  createdAt: '2026-11-01T10:00:00Z',
  acknowledgedAt: null,
  resolvedAt: null,
  ...overrides,
})

const page = (items: unknown[]) => ({ items, total: items.length, page: 1, pageSize: 25 })
const summary = { totalNotifications: 2, unreadNotifications: 1, alertsVisible: true, totalAlerts: 1, activeAlerts: 1, openAlerts: 1, lowStockAlerts: 1, outOfStockAlerts: 0 }
const feedItem = (overrides = {}) => ({
  key: 'alert-5',
  source: 'alert',
  id: 5,
  kind: 'lowStock',
  title: 'Low Stock Alert: Milk',
  message: 'stored english',
  params: { productName: 'Milk', stockQuantity: 3, minimumStock: 10 },
  productId: 9,
  createdAt: new Date().toISOString(),
  isRead: false,
  ...overrides,
})

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'notifications')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  resetBellToasts()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path: string, permissions: string[], feed: { unreadCount: number; items: unknown[] } = { unreadCount: 1, items: [feedItem()] }) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/notifications/feed'] = () => json(feed)
  api.routes['GET /api/v1/notifications/summary'] = () => json(summary)
  api.routes['GET /api/v1/notifications'] = () => json(page([note(1, 'Backup done'), note(2, 'Old news', { isRead: true })]))
  api.routes['GET /api/v1/notifications/alerts'] = () => json(page([alertRow(5)]))
  return mountModule(path, routes)
}

const calledWith = (method: string, path: string) => api.calls.some((c) => c.method === method && c.url.split('?')[0] === `/api/v1${path}`)
const noContent = () => new Response(null, { status: 204 })

describe('notifications page', () => {
  it('lists notifications with the tiles, and marks one read', async () => {
    open('/notifications', [])
    api.routes['POST /api/v1/notifications/1/read'] = noContent

    expect(await screen.findByText('Backup done')).toBeInTheDocument()
    expect(screen.getByText('Old news')).toBeInTheDocument()
    expect(screen.getByText('Total Notifications')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }))

    await waitFor(() => expect(calledWith('POST', '/notifications/1/read')).toBe(true))
    expect(await screen.findByText('Notification marked as read')).toBeInTheDocument()
  })

  it('a user without Alerts.Read has no alerts tab, alert tiles or alert request', async () => {
    open('/notifications', [])
    await screen.findByText('Backup done')

    expect(screen.queryByRole('button', { name: 'Alerts' })).not.toBeInTheDocument()
    expect(screen.queryByText('Active Alerts')).not.toBeInTheDocument()
    expect(calledWith('GET', '/notifications/alerts')).toBe(false)
  })

  it('shows alerts worded from their product facts and dismisses them with Alerts.Update', async () => {
    // Already announced by the bell, so the only copy of the wording on screen is the list row.
    localStorage.setItem('nexpos-notification-toasts-seen', JSON.stringify(['alert-5']))
    open('/notifications', [Permissions.Alerts_View, Permissions.Alerts_Update])
    api.routes['POST /api/v1/notifications/alerts/5/dismiss'] = noContent
    await screen.findByText('Backup done')

    fireEvent.click(screen.getByRole('button', { name: 'Alerts' }))

    expect(await screen.findByText('Low stock: Milk')).toBeInTheDocument()
    expect(screen.getByText('3 left (reorder level 10).')).toBeInTheDocument()
    // No Alerts.Delete: no delete button on the row.
    expect(screen.queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Dismiss alert' }))
    await waitFor(() => expect(calledWith('POST', '/notifications/alerts/5/dismiss')).toBe(true))
  })

  it('asks before clearing everything, and does nothing if the user backs out', async () => {
    open('/notifications', [])
    api.routes['DELETE /api/v1/notifications'] = noContent
    await screen.findByText('Backup done')

    fireEvent.click(screen.getByRole('button', { name: 'Clear All' }))
    const dialog = await screen.findByRole('dialog')
    fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(calledWith('DELETE', '/notifications')).toBe(false)
  })
})

describe('notification bell', () => {
  it('shows the feed count, words items in the user language and acknowledges on click', async () => {
    const router = open('/notifications', [Permissions.Alerts_View])
    api.routes['POST /api/v1/notifications/feed/alert/5/read'] = noContent
    await screen.findByText('Backup done')

    const bell = await screen.findByRole('button', { name: /^Notifications \(/ })
    expect(within(bell).getByText('1')).toBeInTheDocument()
    fireEvent.click(bell)

    const dialog = await screen.findByRole('dialog', { name: 'Notifications' })
    const item = within(dialog).getByRole('button', { name: /Low stock: Milk/ })
    expect(within(item).getByText('3 left (reorder level 10).')).toBeInTheDocument()
    fireEvent.click(item)

    await waitFor(() => expect(calledWith('POST', '/notifications/feed/alert/5/read')).toBe(true))
    // Products is not in this session's nav, so the item falls back to the notifications page.
    expect(router.state.location.pathname).toBe('/notifications')
  })

  it('toasts a new unread item once and remembers it', async () => {
    open('/notifications', [Permissions.Alerts_View])

    expect(await screen.findByRole('button', { name: /Low stock: Milk/ })).toBeInTheDocument()
    await waitFor(() => expect(readSeen()).toContain('alert-5'))
  })

  it('does not toast an item it has already shown', async () => {
    localStorage.setItem('nexpos-notification-toasts-seen', JSON.stringify(['alert-5']))
    open('/notifications', [Permissions.Alerts_View])
    await screen.findByText('Backup done')
    await screen.findByRole('button', { name: /^Notifications \(/ })

    expect(screen.queryByRole('button', { name: /Low stock: Milk/ })).not.toBeInTheDocument()
  })

  it('an alert without product facts keeps its stored text instead of a half-filled sentence', async () => {
    open('/notifications', [Permissions.Alerts_View], { unreadCount: 1, items: [feedItem({ params: {}, title: 'Stock check', message: 'Check the shelves' })] })
    await screen.findByText('Backup done')

    fireEvent.click(await screen.findByRole('button', { name: /^Notifications \(/ }))

    expect((await screen.findAllByText('Stock check')).length).toBeGreaterThan(0)
    expect(screen.getAllByText('Check the shelves').length).toBeGreaterThan(0)
  })

  it('an empty feed says the user is caught up', async () => {
    open('/notifications', [], { unreadCount: 0, items: [] })
    await screen.findByText('Backup done')

    fireEvent.click(await screen.findByRole('button', { name: 'Notifications' }))

    expect(await screen.findByText("You're all caught up")).toBeInTheDocument()
  })
})
