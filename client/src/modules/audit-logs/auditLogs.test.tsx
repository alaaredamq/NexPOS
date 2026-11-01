import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { toQuery, type AuditParams } from './api'
import { hasFilters, readParams, writeParams } from './listParams'
import routes from './routes'

const entry = (id: number, overrides = {}) => ({
  id,
  time: '2026-03-10T12:00:00Z',
  userId: 7,
  user: 'layla',
  role: 'Admin',
  category: 'Data',
  action: 'Update',
  entityType: 'Product',
  entityId: 42,
  label: 'Milk 1L',
  description: 'Changed the price of Milk 1L',
  outcome: 'Success',
  severity: 'Info',
  statusCode: null,
  durationMs: null,
  ...overrides,
})

const page = (items: ReturnType<typeof entry>[]) => ({ items, total: items.length, page: 1, pageSize: 50 })
const stats = { total: 1234, last24Hours: 56, failures24Hours: 3, security24Hours: 9, oldestUtc: '2026-01-02T08:00:00Z' }
const options = {
  users: [
    { userId: 7, username: 'layla' },
    { userId: null, username: 'System' },
  ],
  entityTypes: ['Product', 'Customer'],
  actions: ['Update', 'Login'],
}
const details = (overrides = {}) => ({
  entry: entry(11),
  changes: [
    { field: 'Price', oldValue: '1.5', newValue: '1.75' },
    { field: 'Name', oldValue: 'Milk', newValue: null },
  ],
  additional: '{\n  "reason": "repricing"\n}',
  ipAddress: '10.0.0.5',
  userAgent: 'Edge',
  machine: 'TILL-01',
  appVersion: '1.2.3',
  correlationId: 'req-abc',
  source: 'Web',
  httpMethod: 'PUT',
  path: '/api/v1/products/42',
  rowsInSameRequest: 4,
  ...overrides,
})

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'auditLogs')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path = '/audit-logs', items = [entry(11), entry(12, { user: 'System', userId: null, category: 'Security', outcome: 'Denied', severity: 'Warning', action: 'Login', description: 'Sign-in refused' })]) {
  api = fakeApi(sessionWith(Permissions.AuditLogs_View))
  api.routes['GET /api/v1/audit-logs'] = () => json(page(items))
  api.routes['GET /api/v1/audit-logs/summary'] = () => json(stats)
  api.routes['GET /api/v1/audit-logs/options'] = () => json(options)
  api.routes['GET /api/v1/audit-logs/11'] = () => json(details())
  return mountModule(path, routes)
}

const lastList = () => api.calls.filter((c) => c.method === 'GET' && /^\/api\/v1\/audit-logs\?/.test(c.url)).at(-1)?.url ?? ''

describe('audit log list', () => {
  it('shows the entries with who, type, result and the four tiles', async () => {
    open()

    expect(await screen.findByText('Changed the price of Milk 1L')).toBeInTheDocument()
    expect(screen.getByText('Sign-in refused')).toBeInTheDocument()
    // The System actor is shown in the user's language, the denied result and its severity are badges.
    expect(screen.getAllByText('System').length).toBeGreaterThan(0)
    expect(screen.getByText('Denied')).toBeInTheDocument()
    expect(await screen.findByText('1,234')).toBeInTheDocument()
    expect(screen.getByText('56')).toBeInTheDocument()
    expect(screen.getByText('Since 2026-01-02')).toBeInTheDocument()
  })

  it('without AuditLogs.Read the route bounces to the forbidden page', async () => {
    api = fakeApi(sessionWith(Permissions.Orders_View))
    const router = mountModule('/audit-logs', routes)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('asks for the oldest first when the time header is clicked, and keeps it in the URL', async () => {
    const router = open()
    await screen.findByText('Changed the price of Milk 1L')
    expect(lastList()).toContain('dir=desc')

    fireEvent.click(screen.getByRole('button', { name: /Time/ }))

    await waitFor(() => expect(router.state.location.search).toBe('?dir=asc'))
    await waitFor(() => expect(lastList()).toContain('dir=asc'))
  })

  it('a filter chosen in a drop-down goes to the API and the URL, and Reset clears it', async () => {
    const router = open()
    await screen.findByText('Changed the price of Milk 1L')

    fireEvent.focus(screen.getByRole('combobox', { name: /^Type$/ }))
    fireEvent.click(screen.getByRole('combobox', { name: /^Type$/ }))
    fireEvent.click(await screen.findByRole('option', { name: 'Security' }))

    await waitFor(() => expect(router.state.location.search).toBe('?category=Security'))
    await waitFor(() => expect(lastList()).toContain('category=Security'))

    fireEvent.click(screen.getByRole('button', { name: 'Reset' }))
    await waitFor(() => expect(router.state.location.search).toBe(''))
    expect(screen.getByRole('combobox', { name: /^Type$/ })).toHaveValue('')
    expect(screen.queryByRole('button', { name: 'Reset' })).not.toBeInTheDocument()
  })

  it('the severity filter means "this bad or worse" and is sent as minSeverity', async () => {
    open('/audit-logs?severity=Error')

    await screen.findByText('Changed the price of Milk 1L')

    expect(lastList()).toContain('minSeverity=Error')
    expect(lastList()).not.toContain('severity=Error&')
  })

  it('the record number is applied on Enter, not on every digit', async () => {
    const router = open()
    await screen.findByText('Changed the price of Milk 1L')
    const box = screen.getByLabelText('Record #')

    fireEvent.change(box, { target: { value: '4' } })
    fireEvent.change(box, { target: { value: '42' } })
    expect(router.state.location.search).toBe('')
    fireEvent.keyDown(box, { key: 'Enter' })

    await waitFor(() => expect(router.state.location.search).toBe('?record=42'))
    await waitFor(() => expect(lastList()).toContain('entityId=42'))
  })

  it('"Last hour" turns into a from-instant about an hour ago', async () => {
    open()
    await screen.findByText('Changed the price of Milk 1L')

    fireEvent.click(screen.getByRole('button', { name: 'Last hour' }))

    await waitFor(() => expect(lastList()).toContain('from='))
    const from = new URL(lastList(), 'http://x').searchParams.get('from') as string
    const ageMinutes = (Date.now() - new Date(from).getTime()) / 60_000
    expect(ageMinutes).toBeGreaterThan(55)
    expect(ageMinutes).toBeLessThan(65)
  })

  it('an empty result says so and offers to reset the filters', async () => {
    open('/audit-logs?category=Data', [])

    expect(await screen.findByText('No audit entries match these filters')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Reset' }).length).toBeGreaterThan(0)
  })

  it('a failed load shows the localized message from the server', async () => {
    open()
    api.routes['GET /api/v1/audit-logs'] = () => problem(400, 'bad_request', 'Something broke on the server')

    fireEvent.click(await screen.findByRole('button', { name: 'Refresh' }))

    expect(await screen.findByText('Something broke on the server')).toBeInTheDocument()
  })

  it('exports the current filters as CSV', async () => {
    open('/audit-logs?category=Security')
    await screen.findByText('Changed the price of Milk 1L')
    const created = vi.fn(() => 'blob:csv')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: created, revokeObjectURL: vi.fn() }))
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {})
    api.routes['GET /api/v1/audit-logs/export'] = () =>
      new Response('Time,User', { headers: { 'Content-Type': 'text/csv', 'Content-Disposition': 'attachment; filename="nexpos-audit-1.csv"' } })

    fireEvent.click(screen.getByRole('button', { name: 'Export CSV' }))

    await waitFor(() => expect(click).toHaveBeenCalled())
    expect(created).toHaveBeenCalled()
    const call = api.calls.find((c) => c.url.startsWith('/api/v1/audit-logs/export'))
    expect(call?.url).toContain('category=Security')
    expect(call?.url).not.toContain('page=')
    click.mockRestore()
  })
})

describe('entry drawer', () => {
  it('opens on a row click with the field changes, the extra JSON and where it came from', async () => {
    const router = open()
    fireEvent.click(await screen.findByText('Changed the price of Milk 1L'))

    const drawer = await screen.findByRole('dialog', { name: /Audit entry/ })
    expect(router.state.location.search).toBe('?entry=11')
    expect(await within(drawer).findByText('Price')).toBeInTheDocument()
    // A real before -> after diff, and a field that was cleared shows the empty marker.
    expect(within(drawer).getByText('1.75')).toBeInTheDocument()
    expect(within(drawer).getByText('∅')).toBeInTheDocument()
    expect(within(drawer).getByText(/"reason": "repricing"/)).toBeInTheDocument()
    expect(within(drawer).getByText('TILL-01')).toBeInTheDocument()
    expect(within(drawer).getByText('req-abc')).toBeInTheDocument()
  })

  it('opens straight from a link with ?entry=', async () => {
    open('/audit-logs?entry=11')

    expect(await screen.findByRole('dialog', { name: /Audit entry/ })).toBeInTheDocument()
  })

  it('leaves out "What changed" for a request, which has no old and new values', async () => {
    open('/audit-logs?entry=11')
    api.routes['GET /api/v1/audit-logs/11'] = () => json(details({ entry: entry(11, { category: 'Request' }), changes: [], additional: null }))
    const drawer = await screen.findByRole('dialog', { name: /Audit entry/ })
    await within(drawer).findByText('TILL-01')

    expect(within(drawer).queryByText('What changed')).not.toBeInTheDocument()
    expect(within(drawer).queryByText('Additional details')).not.toBeInTheDocument()
    // ... and it is not "about a record".
    expect(within(drawer).queryByText('Record')).not.toBeInTheDocument()
  })

  it('says so when a data change recorded no field values', async () => {
    api = fakeApi(sessionWith(Permissions.AuditLogs_View))
    api.routes['GET /api/v1/audit-logs'] = () => json(page([entry(11)]))
    api.routes['GET /api/v1/audit-logs/summary'] = () => json(stats)
    api.routes['GET /api/v1/audit-logs/options'] = () => json(options)
    api.routes['GET /api/v1/audit-logs/11'] = () => json(details({ changes: [] }))
    mountModule('/audit-logs?entry=11', routes)

    expect(await screen.findByText('No field-level changes were recorded for this entry.')).toBeInTheDocument()
  })

  it('"Everything in this request" filters by the request id, oldest first, and closes the drawer', async () => {
    const router = open('/audit-logs?entry=11&category=Data')
    const drawer = await screen.findByRole('dialog', { name: /Audit entry/ })

    fireEvent.click(await within(drawer).findByRole('button', { name: 'Everything in this request (4)' }))

    await waitFor(() => expect(router.state.location.search).toBe('?dir=asc&request=req-abc'))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await waitFor(() => expect(lastList()).toContain('correlationId=req-abc'))
    // The category filter from before is gone: a drill-down starts clean. The chip shows what the list is limited to.
    expect(lastList()).not.toContain('category=')
    expect(screen.getByText('req-abc')).toBeInTheDocument()
  })

  it('"History of this record" and "Everything by this user" drill down', async () => {
    const router = open('/audit-logs?entry=11')
    let drawer = await screen.findByRole('dialog', { name: /Audit entry/ })

    fireEvent.click(await within(drawer).findByRole('button', { name: 'History of this record' }))
    await waitFor(() => expect(router.state.location.search).toBe('?type=Product&record=42'))

    fireEvent.click(await screen.findByText('Changed the price of Milk 1L'))
    drawer = await screen.findByRole('dialog', { name: /Audit entry/ })
    fireEvent.click(await within(drawer).findByRole('button', { name: 'Everything by this user' }))
    await waitFor(() => expect(router.state.location.search).toBe('?user=7'))

  })

  it('an actor without an account is found by name', async () => {
    const router = open('/audit-logs?entry=13')
    api.routes['GET /api/v1/audit-logs/13'] = () => json(details({ entry: entry(13, { user: 'ghost', userId: null }) }))
    const drawer = await screen.findByRole('dialog', { name: /Audit entry/ })

    fireEvent.click(await within(drawer).findByRole('button', { name: 'Everything by this user' }))

    await waitFor(() => expect(router.state.location.search).toBe('?user=name%3Aghost'))
    await waitFor(() => expect(lastList()).toContain('username=ghost'))
  })

  it('Escape closes it and drops ?entry from the URL', async () => {
    const router = open('/audit-logs?entry=11')
    await screen.findByRole('dialog', { name: /Audit entry/ })

    fireEvent.keyDown(document, { key: 'Escape' })

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(router.state.location.search).toBe('')
  })

  it('copies the request id', async () => {
    open('/audit-logs?entry=11')
    const write = vi.fn(async () => {})
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: write }, configurable: true })
    const drawer = await screen.findByRole('dialog', { name: /Audit entry/ })

    fireEvent.click(await within(drawer).findByRole('button', { name: 'Copy request ID' }))

    await waitFor(() => expect(write).toHaveBeenCalledWith('req-abc'))
  })

  it('an entry that no longer exists says it could not be loaded', async () => {
    open('/audit-logs?entry=11')
    api.routes['GET /api/v1/audit-logs/11'] = () => problem(404, 'not_found')

    expect(await screen.findByText('The entry could not be loaded.')).toBeInTheDocument()
  })
})

describe('URL state and query building', () => {
  const base: AuditParams = {
    page: 1, pageSize: 50, dir: 'desc', q: '', from: '', to: '', user: '', category: '', outcome: '', severity: '',
    entityType: '', entityId: '', action: '', request: '', entry: null,
  }

  it('turns the picked local times into UTC instants, with the whole last minute included in "to"', () => {
    const q = toQuery({ ...base, from: '2026-03-10T14:30', to: '2026-03-10T15:00' })

    expect(q.from).toBe(new Date('2026-03-10T14:30').toISOString())
    expect(new Date(q.to as string).getTime() - new Date('2026-03-10T15:00').getTime()).toBe(60_000)
  })

  it('splits the user filter into an id or, for actors without an account, a name', () => {
    expect(toQuery({ ...base, user: '7' })).toMatchObject({ userId: 7, username: undefined })
    expect(toQuery({ ...base, user: 'name:System' })).toMatchObject({ userId: undefined, username: 'System' })
    expect(toQuery({ ...base, entityId: '42', request: 'abc', severity: 'Error' })).toMatchObject({ entityId: 42, correlationId: 'abc', minSeverity: 'Error' })
  })

  it('round-trips through the URL and leaves defaults out', () => {
    const p: AuditParams = { ...base, page: 3, pageSize: 100, dir: 'asc', q: 'milk', category: 'Data', user: 'name:System', entityType: 'Product', entityId: '5', entry: 9 }

    const url = writeParams(p)

    expect(readParams(url)).toEqual(p)
    expect(writeParams(base).toString()).toBe('')
  })

  it('ignores values it does not know instead of sending them to the API', () => {
    const p = readParams(new URLSearchParams('page=-2&size=7&dir=sideways&category=Banana&outcome=Maybe&severity=Info&record=abc&user=x&from=yesterday&entry=0'))

    expect(p).toEqual(base)
    expect(hasFilters(p)).toBe(false)
    expect(hasFilters({ ...base, request: 'x' })).toBe(true)
  })
})
