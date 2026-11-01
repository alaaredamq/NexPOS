import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { formatBytes } from './bytes'
import routes from './routes'

const backup = (id: number, overrides = {}) => ({
  id,
  name: `Nightly ${id}`,
  description: null,
  fileName: `ERP_full_backup_2026110${id}_020000.bak`,
  fileSizeBytes: 1_572_864,
  status: 'Completed',
  createdAt: '2026-11-01T02:00:00Z',
  completedAt: '2026-11-01T02:00:03Z',
  durationSeconds: 3,
  errorMessage: null,
  ...overrides,
})

const page = (items: ReturnType<typeof backup>[]) => ({ items, total: items.length, page: 1, pageSize: 25 })

const summary = {
  totalBackups: 3,
  successfulBackups: 2,
  failedBackups: 1,
  totalSizeBytes: 4_718_592,
  scheduledCount: 1,
  nextScheduledAt: '2026-11-02T00:00:00Z',
  lastBackupAt: '2026-11-01T02:00:03Z',
  lastBackupStatus: 'Completed',
  backupDirectory: 'C:\\Users\\shop\\AppData\\Local\\NexPOS\\Backups',
  availableDiskBytes: 53_687_091_200,
  databaseSizeBytes: 2_097_152,
}

const schedule = (id: number, overrides = {}) => ({
  id,
  name: `Evening ${id}`,
  description: null,
  frequency: 'Weekly',
  time: '23:30',
  dayOfWeek: 3,
  dayOfMonth: null,
  isActive: true,
  retentionDays: 14,
  lastRunAt: null,
  nextRunAt: '2026-11-04T21:30:00Z',
  ...overrides,
})

const ALL = [Permissions.Backup_View, Permissions.Backup_Create, Permissions.Backup_Restore, Permissions.Backup_Manage]

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'backup')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path = '/backup', permissions: string[] = ALL, items = [backup(1), backup(2, { status: 'Failed', errorMessage: 'Disk is full' })]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/backups'] = () => json(page(items))
  api.routes['GET /api/v1/backups/summary'] = () => json(summary)
  api.routes['GET /api/v1/backup-schedules'] = () => json([schedule(7)])
  return mountModule(path, routes)
}

const row = (name: string | RegExp) => screen.getByText(name).closest('tr') as HTMLElement
const callsTo = (method: string, url: string) => api.calls.filter((c) => c.method === method && c.url.replace(/\?.*$/, '') === url)

describe('formatBytes', () => {
  it('uses 1024 steps with one decimal, and keeps the figure together in RTL text', () => {
    expect(formatBytes(0)).toContain('0 B')
    expect(formatBytes(1536)).toContain('1.5 KB')
    expect(formatBytes(1_572_864)).toContain('1.5 MB')
    expect(formatBytes(53_687_091_200)).toContain('50.0 GB')
  })
})

describe('backup list', () => {
  it('shows each backup with its size and status, the failure reason, and the four tiles', async () => {
    open()

    expect(await screen.findByText('Nightly 1')).toBeInTheDocument()
    expect(within(row('Nightly 1')).getByText('Completed')).toBeInTheDocument()
    expect(within(row('Nightly 1')).getByText(/1\.5 MB/)).toBeInTheDocument()
    expect(within(row('Nightly 2')).getByText('Failed')).toBeInTheDocument()
    expect(within(row('Nightly 2')).getByText('Disk is full')).toBeInTheDocument()
    expect(await screen.findByText(/4\.5 MB/)).toBeInTheDocument() // total size tile
    expect(screen.getByText('2 Successful')).toBeInTheDocument()
  })

  it('without Backup.View the route bounces to the forbidden page', async () => {
    api = fakeApi(sessionWith(Permissions.Orders_View))
    const router = mountModule('/backup', routes)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('a viewer can download but not create, validate, delete or restore', async () => {
    open('/backup', [Permissions.Backup_View])
    await screen.findByText('Nightly 1')

    expect(screen.queryByRole('button', { name: 'Create Backup' })).not.toBeInTheDocument()
    expect(within(row('Nightly 1')).getByRole('button', { name: 'Download' })).toBeInTheDocument()
    expect(within(row('Nightly 1')).queryByRole('button', { name: 'Validate' })).not.toBeInTheDocument()
    expect(within(row('Nightly 1')).queryByRole('button', { name: 'Delete' })).not.toBeInTheDocument()
    expect(screen.queryByRole('link', { name: 'Restore' })).not.toBeInTheDocument()
  })

  it('a failed backup cannot be downloaded', async () => {
    open()
    await screen.findByText('Nightly 2')

    expect(within(row('Nightly 2')).getByRole('button', { name: 'Download' })).toBeDisabled()
    expect(within(row('Nightly 1')).getByRole('button', { name: 'Download' })).toBeEnabled()
  })

  it('checks one backup and tells the result', async () => {
    open()
    api.routes['POST /api/v1/backups/1/validate'] = () => json({ isValid: true, errors: [], warnings: [], checksum: 'abc', fileSizeBytes: 1 })
    await screen.findByText('Nightly 1')

    fireEvent.click(within(row('Nightly 1')).getByRole('button', { name: 'Validate' }))

    expect(await screen.findByText('Backup is valid')).toBeInTheDocument()
  })

  it('asks before deleting one backup, then deletes it', async () => {
    open()
    api.routes['DELETE /api/v1/backups/1'] = () => new Response(null, { status: 204 })
    await screen.findByText('Nightly 1')

    fireEvent.click(within(row('Nightly 1')).getByRole('button', { name: 'Delete' }))
    const dialog = await screen.findByRole('dialog', { name: 'Delete Backup' })
    expect(within(dialog).getByText('Nightly 1')).toBeInTheDocument()
    expect(callsTo('DELETE', '/api/v1/backups/1')).toHaveLength(0)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes, Delete' }))

    await waitFor(() => expect(callsTo('DELETE', '/api/v1/backups/1')).toHaveLength(1))
    expect(await screen.findByText('Backup deleted successfully')).toBeInTheDocument()
  })

  it('selects several and deletes them in one call', async () => {
    open()
    api.routes['POST /api/v1/backups/bulk-delete'] = () => json({ deleted: 2 })
    await screen.findByText('Nightly 1')

    fireEvent.click(screen.getByRole('checkbox', { name: 'Select all' }))
    expect(await screen.findByText('2 selected')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Bulk Delete' }))
    const dialog = await screen.findByRole('dialog', { name: 'Delete Multiple Backups' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Yes, Delete' }))

    await waitFor(() => expect(callsTo('POST', '/api/v1/backups/bulk-delete')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/backups/bulk-delete')[0]!.body).toEqual({ ids: [1, 2] })
    expect(await screen.findByText('2 backup(s) deleted')).toBeInTheDocument()
  })

  it('the status chips filter on the server', async () => {
    open()
    await screen.findByText('Nightly 1')

    fireEvent.click(screen.getByRole('button', { name: 'Failed' }))

    await waitFor(() => expect(api.calls.some((c) => /\/api\/v1\/backups\?.*status=Failed/.test(c.url))).toBe(true))
  })
})

describe('create backup', () => {
  it('creates a backup with the name given and closes when it is done', async () => {
    open()
    api.routes['POST /api/v1/backups'] = () => json(backup(9, { name: 'Before the sale' }), 201)
    await screen.findByText('Nightly 1')

    fireEvent.click(screen.getAllByRole('button', { name: 'Create Backup' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Create New Backup' })
    fireEvent.change(within(dialog).getByLabelText('Backup Name'), { target: { value: ' Before the sale ' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create Backup' }))

    await waitFor(() => expect(callsTo('POST', '/api/v1/backups')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/backups')[0]!.body).toEqual({ name: 'Before the sale' })
    expect(await screen.findByText('Backup completed successfully')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog', { name: 'Create New Backup' })).not.toBeInTheDocument())
  })

  it('a backup that fails shows the reason inside the dialog, which stays open', async () => {
    open()
    api.routes['POST /api/v1/backups'] = () => problem(500, 'backup_failed', 'Disk is full')
    await screen.findByText('Nightly 1')

    fireEvent.click(screen.getAllByRole('button', { name: 'Create Backup' })[0]!)
    const dialog = await screen.findByRole('dialog', { name: 'Create New Backup' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Create Backup' }))

    expect(await within(dialog).findByText('Disk is full')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Create New Backup' })).toBeInTheDocument()
  })
})

describe('restore', () => {
  const pick = async (name: string) => {
    const combo = await screen.findByRole('combobox', { name: /Select Backup/ })
    fireEvent.focus(combo)
    fireEvent.click(combo)
    fireEvent.click(await screen.findByRole('option', { name: new RegExp(name) }))
  }

  it('needs Backup.Restore to open', async () => {
    api = fakeApi(sessionWith(Permissions.Backup_View))
    api.routes['GET /api/v1/backups/summary'] = () => json(summary)
    const router = mountModule('/backup/restore', routes)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('asks first and names what will be lost, then restores with a safety backup by default', async () => {
    open('/backup/restore')
    api.routes['POST /api/v1/backups/1/restore'] = () => new Response(null, { status: 204 })
    await pick('Nightly 1')
    const sessionCalls = () => api.calls.filter((c) => c.url === '/api/v1/session').length
    const before = sessionCalls()

    fireEvent.click(screen.getByRole('button', { name: 'Start Restore' }))
    const dialog = await screen.findByRole('dialog', { name: 'Restore this backup?' })
    expect(within(dialog).getByText(/will be replaced by the data in "Nightly 1"/)).toBeInTheDocument()
    expect(within(dialog).getByText(/backup of the current data is taken first/)).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/backups/1/restore')).toHaveLength(0)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }))

    await waitFor(() => expect(callsTo('POST', '/api/v1/backups/1/restore')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/backups/1/restore')[0]!.body).toEqual({ createBackupBeforeRestore: true })
    expect(await screen.findByText(/Restore completed/)).toBeInTheDocument()
    // Everything the app knew is from before the restore: the session (user, permissions) is read again.
    await waitFor(() => expect(sessionCalls()).toBeGreaterThan(before))
  })

  it('without the safety backup the dialog says it cannot be undone, and the request says so', async () => {
    open('/backup/restore')
    api.routes['POST /api/v1/backups/1/restore'] = () => new Response(null, { status: 204 })
    await pick('Nightly 1')

    fireEvent.click(screen.getByRole('checkbox', { name: 'Create backup before restore' }))
    fireEvent.click(screen.getByRole('button', { name: 'Start Restore' }))
    const dialog = await screen.findByRole('dialog', { name: 'Restore this backup?' })
    expect(within(dialog).getByText(/cannot be undone/)).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }))

    await waitFor(() => expect(callsTo('POST', '/api/v1/backups/1/restore')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/backups/1/restore')[0]!.body).toEqual({ createBackupBeforeRestore: false })
  })

  it('a restore that fails shows the reason in the dialog, which stays open', async () => {
    open('/backup/restore')
    api.routes['POST /api/v1/backups/1/restore'] = () => problem(409, 'restore_failed', 'Backup file not found')
    await pick('Nightly 1')

    fireEvent.click(screen.getByRole('button', { name: 'Start Restore' }))
    const dialog = await screen.findByRole('dialog', { name: 'Restore this backup?' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Restore' }))

    expect(await within(dialog).findByText('Backup file not found')).toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'Restore this backup?' })).toBeInTheDocument()
  })

  it('only completed backups are offered', async () => {
    open('/backup/restore')
    await pick('Nightly 1')

    expect(api.calls.some((c) => /\/api\/v1\/backups\?.*status=Completed/.test(c.url))).toBe(true)
  })
})

describe('schedules', () => {
  it('describes when each one runs and what it keeps', async () => {
    open('/backup/schedules')

    expect(await screen.findByText('Evening 7')).toBeInTheDocument()
    expect(screen.getByText('Weekly, Wednesday at 23:30')).toBeInTheDocument()
    expect(screen.getByText('Keeps 14 days')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
  })

  it('pauses a schedule', async () => {
    open('/backup/schedules')
    api.routes['PUT /api/v1/backup-schedules/7/active'] = () => json(schedule(7, { isActive: false, nextRunAt: null }))
    await screen.findByText('Evening 7')

    fireEvent.click(within(row('Evening 7')).getByRole('button', { name: 'Disable' }))

    await waitFor(() => expect(callsTo('PUT', '/api/v1/backup-schedules/7/active')).toHaveLength(1))
    expect(callsTo('PUT', '/api/v1/backup-schedules/7/active')[0]!.body).toEqual({ isActive: false })
  })

  it('runs one now after asking', async () => {
    open('/backup/schedules')
    api.routes['POST /api/v1/backup-schedules/7/run'] = () => new Response(null, { status: 204 })
    await screen.findByText('Evening 7')

    fireEvent.click(within(row('Evening 7')).getByRole('button', { name: 'Run Now' }))
    const dialog = await screen.findByRole('dialog', { name: 'Run Now' })
    expect(callsTo('POST', '/api/v1/backup-schedules/7/run')).toHaveLength(0)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Run Now' }))

    await waitFor(() => expect(callsTo('POST', '/api/v1/backup-schedules/7/run')).toHaveLength(1))
  })

  it('creates a monthly schedule, and refuses to save without a name', async () => {
    open('/backup/schedules')
    api.routes['POST /api/v1/backup-schedules'] = () => json(schedule(8, { frequency: 'Monthly' }), 201)
    await screen.findByText('Evening 7')

    fireEvent.click(screen.getByRole('button', { name: 'Create Schedule' }))
    const dialog = await screen.findByRole('dialog', { name: 'Create Schedule' })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))
    expect(await within(dialog).findByText('Please enter a schedule name')).toBeInTheDocument()
    expect(callsTo('POST', '/api/v1/backup-schedules')).toHaveLength(0)

    fireEvent.change(within(dialog).getByLabelText(/Schedule Name/), { target: { value: 'Month end' } })
    const frequency = within(dialog).getByRole('combobox', { name: /Frequency/ })
    fireEvent.focus(frequency)
    fireEvent.click(frequency)
    fireEvent.click(await screen.findByRole('option', { name: 'Monthly' }))
    fireEvent.change(within(dialog).getByLabelText(/^Time/), { target: { value: '03:15' } })
    fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(callsTo('POST', '/api/v1/backup-schedules')).toHaveLength(1))
    expect(callsTo('POST', '/api/v1/backup-schedules')[0]!.body).toEqual({
      name: 'Month end',
      description: null,
      frequency: 'Monthly',
      time: '03:15',
      dayOfWeek: null,
      dayOfMonth: 1,
      retentionDays: null,
      isActive: true,
    })
  })

  it('a viewer sees the schedules but none of the controls', async () => {
    open('/backup/schedules', [Permissions.Backup_View])
    await screen.findByText('Evening 7')

    expect(screen.queryByRole('button', { name: 'Create Schedule' })).not.toBeInTheDocument()
    expect(within(row('Evening 7')).queryByRole('button')).not.toBeInTheDocument()
  })
})

describe('system', () => {
  it('shows where the backups are kept and how much room is left, left-to-right inside Arabic text', async () => {
    open('/backup/system')

    const folder = await screen.findByText('C:\\Users\\shop\\AppData\\Local\\NexPOS\\Backups')
    expect(folder).toHaveAttribute('dir', 'ltr')
    expect(screen.getByText(/50\.0 GB/)).toBeInTheDocument()
    expect(screen.getByText(/Copy this folder to another disk/)).toBeInTheDocument()
  })
})

describe('Arabic', () => {
  it('renders the page in Arabic', async () => {
    await setLanguage('ar')
    open()

    expect(await screen.findByText('إدارة النسخ الاحتياطي والاستعادة')).toBeInTheDocument()
    await setLanguage('en')
  })
})
