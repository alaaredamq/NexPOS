import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const notActivated = {
  status: 'NotActivated',
  severity: 'danger',
  title: 'Not activated',
  message: 'Coduo POS is not activated on this computer, so the POS and new sales are stopped.',
  allowsSales: false,
  showsWarning: false,
  enforcementDisabled: false,
  machineId: 'ABCDE-12345-FGHIJ-67890',
  buildDate: '2026-10-01',
  customer: null,
  kind: null,
  serial: null,
  seats: 1,
  issuedOn: null,
  validUntil: null,
  isTimeLimited: false,
  daysLeft: null,
  daysUntilStop: null,
  salesStopAfter: null,
}

const active = {
  ...notActivated,
  status: 'Active',
  severity: 'success',
  title: 'Licensed',
  message: 'Coduo POS is activated on this computer.',
  allowsSales: true,
  customer: 'Corner Shop',
  kind: 'Trial',
  serial: 'CAFE0001',
  issuedOn: '2026-11-01',
  validUntil: '2026-12-01',
  isTimeLimited: true,
}

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(state: object = notActivated, permissions: string[] = [Permissions.License_Manage]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/license'] = () => json(state)
  return mountModule('/license', routes)
}

describe('licence page', () => {
  it('opens although selling is stopped, and says so with the machine id to send to the provider', async () => {
    open()

    expect(await screen.findByRole('heading', { name: 'License', level: 2 })).toBeInTheDocument()
    expect(screen.getByText('Not activated')).toBeInTheDocument()
    expect(screen.getByText(/so the POS and new sales are stopped/)).toBeInTheDocument()
    expect(screen.getByLabelText('Machine ID')).toHaveValue('ABCDE-12345-FGHIJ-67890')
    expect(screen.getByLabelText('Machine ID').closest('div')).toHaveAttribute('dir', 'ltr')
    // No licence yet: no customer / serial block.
    expect(screen.queryByText('Serial')).not.toBeInTheDocument()
  })

  it('needs License.Manage', async () => {
    api = fakeApi(sessionWith(Permissions.Orders_View))
    const router = mountModule('/license', routes)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('shows the customer, type, serial and dates of an installed licence; a time-limited one says "Expires on"', async () => {
    open(active)

    expect(await screen.findByText('Corner Shop')).toBeInTheDocument()
    expect(screen.getByText('Trial')).toBeInTheDocument()
    expect(screen.getByText('CAFE0001')).toBeInTheDocument()
    expect(screen.getByText('Expires on')).toBeInTheDocument()
    expect(screen.queryByText('Updates until')).not.toBeInTheDocument()
  })

  it('says how many PCs the licence covers', async () => {
    open({ ...active, seats: 5 })

    expect(await screen.findByText('PCs covered')).toBeInTheDocument()
    expect(screen.getByText('5')).toBeInTheDocument()
  })

  it('a full licence says "Updates until"', async () => {
    open({ ...active, kind: 'Full', isTimeLimited: false })

    expect(await screen.findByText('Updates until')).toBeInTheDocument()
    expect(screen.queryByText('Expires on')).not.toBeInTheDocument()
  })

  it('copies the machine id', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.assign(navigator, { clipboard: { writeText } })
    open()

    fireEvent.click(await screen.findByRole('button', { name: 'Copy' }))

    await waitFor(() => expect(writeText).toHaveBeenCalledWith('ABCDE-12345-FGHIJ-67890'))
    expect(await screen.findByRole('button', { name: 'Copied' })).toBeInTheDocument()
  })
})

describe('activation', () => {
  it('sends the pasted key trimmed, shows the new state and refreshes the session so the banner and sales routes follow', async () => {
    open()
    api.routes['POST /api/v1/license/activate'] = () => json(active)
    const sessionCalls = () => api.calls.filter((c) => c.url === '/api/v1/session').length
    const textbox = await screen.findByRole('textbox', { name: 'Activate or renew' })
    const before = sessionCalls()

    expect(screen.getByRole('button', { name: 'Activate' })).toBeDisabled()
    fireEvent.change(textbox, { target: { value: '  KEY-1234  ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Activate' }))

    expect(await screen.findByText('License activated successfully.')).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ licenseKey: 'KEY-1234' })
    expect(await screen.findByText('Corner Shop')).toBeInTheDocument()
    expect(textbox).toHaveValue('')
    await waitFor(() => expect(sessionCalls()).toBeGreaterThan(before))
  })

  it('a refused key shows the reason under the box and changes nothing', async () => {
    open()
    api.routes['POST /api/v1/license/activate'] = () =>
      json(
        {
          status: 400,
          title: 'Validation failed',
          code: 'validation_failed',
          errors: { licenseKey: ['This license key was issued for a different computer.'] },
        },
        400,
      )
    const textbox = await screen.findByRole('textbox', { name: 'Activate or renew' })

    fireEvent.change(textbox, { target: { value: 'KEY-OTHER-PC' } })
    fireEvent.click(screen.getByRole('button', { name: 'Activate' }))

    const alert = await screen.findByRole('alert')
    expect(within(alert).getByText('This license key was issued for a different computer.')).toBeInTheDocument()
    expect(textbox).toHaveValue('KEY-OTHER-PC')
    expect(screen.queryByText('License activated successfully.')).not.toBeInTheDocument()
    expect(screen.getByText('Not activated')).toBeInTheDocument()
  })
})

describe('tills of a multi-seat package', () => {
  const package3 = { ...active, kind: 'Full', isTimeLimited: false, seats: 3 }
  const noTills = { seats: 3, tillSeats: 2, used: 0, enforced: true, devices: [] as object[] }
  const nameBox = 'Name of the new till (for example Till 2)'
  const device = (over: object = {}) => ({
    id: 7,
    name: 'Till 2',
    state: 'Active',
    createdAt: '2026-11-06T09:00:00Z',
    pairedAt: '2026-11-06T09:05:00Z',
    lastSeenAt: '2026-11-06T10:00:00Z',
    pairingExpiresAt: null,
    overflow: false,
    ...over,
  })

  function openWithTills(tills: object = noTills) {
    const view = open(package3)
    api.routes['GET /api/v1/devices'] = () => json(tills)
    return view
  }

  it('is not shown for a one-PC licence', async () => {
    open({ ...active, seats: 1 })

    expect(await screen.findByText('Corner Shop')).toBeInTheDocument()
    expect(screen.queryByText('PCs in this package')).not.toBeInTheDocument()
    expect(api.calls.some((c) => c.url === '/api/v1/devices')).toBe(false)
  })

  it('counts the server as one of the PCs in use and lists each till with its state', async () => {
    openWithTills({
      ...noTills,
      used: 2,
      devices: [
        device(),
        device({ id: 8, name: 'Back counter', state: 'Pending', pairedAt: null, lastSeenAt: null, pairingExpiresAt: '2026-11-06T11:00:00Z' }),
      ],
    })

    expect(await screen.findByText('3 of 3 PCs in use. This server counts as one.')).toBeInTheDocument()
    expect(screen.getByText('Till 2')).toBeInTheDocument()
    expect(screen.getByText('Paired')).toBeInTheDocument()
    expect(screen.getByText('Back counter')).toBeInTheDocument()
    expect(screen.getByText('Waiting for code')).toBeInTheDocument()
  })

  it('adding a till shows the one-time code and where to type it', async () => {
    openWithTills()
    api.routes['POST /api/v1/devices/pairing-codes'] = () => json({ deviceId: 9, code: 'K7MQ-2XWP', expiresAt: '2026-11-06T11:00:00Z' })
    const name = await screen.findByRole('textbox', { name: nameBox })
    expect(screen.getByRole('button', { name: 'Add a till' })).toBeDisabled()

    fireEvent.change(name, { target: { value: '  Till 2  ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add a till' }))

    expect(await screen.findByText('K7MQ-2XWP')).toBeInTheDocument()
    expect(screen.getByText(/\/app\/pair-till and type this code/)).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === 'POST' && c.url.endsWith('pairing-codes'))?.body).toEqual({ name: 'Till 2' })
  })

  it('a full package says so and cannot add another till', async () => {
    openWithTills({ ...noTills, used: 2, devices: [device(), device({ id: 8, name: 'Till 3' })] })

    expect(await screen.findByText(/All the PCs in your package are in use/)).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: nameBox })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Add a till' })).toBeDisabled()
  })

  it("a refused code request shows the server's reason", async () => {
    openWithTills()
    api.routes['POST /api/v1/devices/pairing-codes'] = () =>
      json({ status: 409, title: 'Conflict', code: 'seats_full', detail: 'Every PC of the package is taken.' }, 409)
    const name = await screen.findByRole('textbox', { name: nameBox })

    fireEvent.change(name, { target: { value: 'Till 2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add a till' }))

    expect(within(await screen.findByRole('alert')).getByText('Every PC of the package is taken.')).toBeInTheDocument()
  })

  it('removing a till asks first, then frees the seat', async () => {
    openWithTills({ ...noTills, used: 1, devices: [device()] })
    api.routes['DELETE /api/v1/devices/7'] = () => new Response(null, { status: 204 })
    fireEvent.click(await screen.findByRole('button', { name: 'Remove till' }))

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByText('Remove "Till 2"? It will stop being able to sell.')).toBeInTheDocument()
    fireEvent.click(within(dialog).getByRole('button', { name: 'Remove till' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'DELETE' && c.url === '/api/v1/devices/7')).toBe(true))
  })

  it('flags a till that is over the limit after a smaller package was installed', async () => {
    openWithTills({ ...noTills, used: 2, devices: [device(), device({ id: 8, name: 'Till 3', overflow: true })] })

    expect(await screen.findByText('Over the limit')).toBeInTheDocument()
  })
})

describe('which PC is looking', () => {
  it('on a till it says the licence and ID belong to the store server and leaves activating to the server', async () => {
    open(active)
    api.routes['GET /api/v1/devices/me'] = () => json({ isServer: false, access: 'Allowed' })

    expect(await screen.findByText(/This PC is a till of your store server/)).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Store server ID' })).toHaveValue('ABCDE-12345-FGHIJ-67890')
    expect(screen.queryByRole('textbox', { name: 'Activate or renew' })).not.toBeInTheDocument()
    expect(screen.queryByRole('textbox', { name: 'Machine ID' })).not.toBeInTheDocument()
  })

  it('on the store server it is the plain Machine ID and the activation box', async () => {
    open(active)
    api.routes['GET /api/v1/devices/me'] = () => json({ isServer: true, access: 'Allowed' })

    expect(await screen.findByRole('textbox', { name: 'Machine ID' })).toBeInTheDocument()
    expect(screen.getByRole('textbox', { name: 'Activate or renew' })).toBeInTheDocument()
    expect(screen.queryByText(/This PC is a till of your store server/)).not.toBeInTheDocument()
  })
})
