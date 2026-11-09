import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, problem, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const off = (overrides = {}) => ({ connected: false, pendingChanges: 0, ...overrides })

const on = (overrides = {}) => ({
  connected: true,
  cloudHost: 'cloud.example.com',
  siteName: 'Main till',
  clientName: 'Corner Shop',
  connectedAtUtc: '2026-11-06T08:00:00Z',
  lastAttemptUtc: '2026-11-06T09:00:00Z',
  lastSuccessUtc: '2026-11-06T09:00:00Z',
  pendingChanges: 0,
  subscriptionStatus: 'Active',
  subscriptionExpiresAtUtc: '2027-02-06T00:00:00Z',
  sealingState: 'on',
  sealKeyId: 'ab12cd34ef56ab12',
  ...overrides,
})

const LINK = 'https://cloud.example.com/enroll/7K4M-Q2PD-X9RT'

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'settings')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(status: object, ...permissions: string[]) {
  api = fakeApi(sessionWith(...(permissions.length ? permissions : [Permissions.Settings_Manage])))
  api.routes['GET /api/v1/online'] = () => json(status)
  return mountModule('/settings/online', routes)
}

const calls = (method: string, url: string) => api.calls.filter((c) => c.method === method && c.url.startsWith(url))

describe('access', () => {
  it('needs Settings.Manage like the other settings tabs', async () => {
    const router = open(off(), Permissions.Settings_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
    expect(calls('GET', '/api/v1/online')).toHaveLength(0)
  })

  it('is one of the four settings tabs', async () => {
    open(off())

    expect(await screen.findByRole('link', { name: 'Online' })).toHaveAttribute('href', '/settings/online')
  })
})

describe('not connected', () => {
  it('explains the three steps and offers a box for the link', async () => {
    open(off())

    expect(await screen.findByLabelText('Connection link')).toBeInTheDocument()
    expect(screen.getByText('Ask your provider for your connection link.')).toBeInTheDocument()
    expect(screen.getByText(/Selling never waits for Online/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Connect' })).toBeInTheDocument()
  })

  it('sends what was pasted, trimmed, and shows the connected view without a reload', async () => {
    open(off())
    api.routes['POST /api/v1/online/connect'] = () => json(on())
    fireEvent.change(await screen.findByLabelText('Connection link'), { target: { value: `  ${LINK}\n` } })

    fireEvent.click(screen.getByRole('button', { name: 'Connect' }))

    expect(await screen.findByText('Corner Shop')).toBeInTheDocument()
    expect(calls('POST', '/api/v1/online/connect')[0]!.body).toEqual({ link: LINK })
    expect(await screen.findByText('Connected to NexPOS Online')).toBeInTheDocument()
    expect(screen.queryByLabelText('Connection link')).not.toBeInTheDocument()
  })

  it('does not call the server for an empty box', async () => {
    open(off())

    fireEvent.click(await screen.findByRole('button', { name: 'Connect' }))

    expect(await screen.findByText(/doesn't look like a NexPOS Online link/)).toBeInTheDocument()
    expect(calls('POST', '/api/v1/online/connect')).toHaveLength(0)
  })

  it.each([
    [400, 'enroll_code_used', 'This link was already used. Ask your provider for a new one.'],
    [400, 'enroll_code_expired', 'This link has expired. Ask your provider for a new one.'],
    [400, 'enroll_code_invalid', 'This link is not valid. Check that you copied all of it, or ask for a new one.'],
    [400, 'cloud_insecure_url', 'This link does not start with https:// so it cannot be trusted. Ask your provider for a new link.'],
    [502, 'cloud_unreachable', "Could not reach the cloud. Check this computer's internet connection and try again."],
    [400, 'rate_limited', 'Too many attempts. Wait a few minutes and try again.'],
  ])('says in plain words what went wrong (%s %s)', async (status, code, sentence) => {
    open(off())
    api.routes['POST /api/v1/online/connect'] = () => problem(status, code)
    fireEvent.change(await screen.findByLabelText('Connection link'), { target: { value: LINK } })

    fireEvent.click(screen.getByRole('button', { name: 'Connect' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
    expect(screen.getByLabelText('Connection link')).toHaveValue(LINK) // keeps what was typed so a typo can be fixed
  })

  it('warns when the saved settings file could not be read', async () => {
    open(off({ settingsProblem: 'Unexpected character' }))

    expect(await screen.findByText(/saved Online settings could not be read/)).toBeInTheDocument()
  })
})

describe('connected', () => {
  it('shows the shop, the last send, what is waiting and the subscription', async () => {
    open(on({ pendingChanges: 3 }))

    expect(await screen.findByText('Corner Shop')).toBeInTheDocument()
    expect(screen.getByText('Main till')).toBeInTheDocument()
    expect(screen.getByText('Connected')).toBeInTheDocument()
    expect(screen.getByText('Changes waiting to be sent: 3')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(screen.getByText('cloud.example.com')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('says everything was sent when nothing is waiting and the first sync is pending before any success', async () => {
    open(on({ lastSuccessUtc: null }))

    expect(await screen.findByText('Everything has been sent')).toBeInTheDocument()
    expect(screen.getByText('Waiting for the first sync…')).toBeInTheDocument()
  })

  it.each([
    ['network', /Can't reach the cloud right now/],
    ['timeout', /Can't reach the cloud right now/],
    ['subscription_inactive', /subscription has ended or is paused/],
    ['unauthorized', /Your provider disconnected this till/],
    ['schema_newer', /newer than the cloud service/],
    ['server_error', /could not accept the latest changes/],
  ])('turns the code %s into advice, and reassures that nothing is lost', async (code, sentence) => {
    open(on({ lastErrorCode: code, lastError: 'English detail for support', pendingChanges: 5 }))

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent(sentence)
    expect(screen.getByText('Needs attention')).toBeInTheDocument()
    expect(screen.queryByText('English detail for support')).not.toBeInTheDocument()
  })

  it('shows an ended subscription as a problem and a grace period as a warning', async () => {
    open(on({ subscriptionStatus: 'Grace' }))
    expect(await screen.findByText('Ended, grace period')).toBeInTheDocument()
  })

  it('"Send now" asks the till to sync and then looks again', async () => {
    open(on())
    api.routes['POST /api/v1/online/sync-now'] = () => json(on())
    fireEvent.click(await screen.findByRole('button', { name: 'Send now' }))

    await waitFor(() => expect(calls('POST', '/api/v1/online/sync-now')).toHaveLength(1))
  })

  it('disconnecting asks first, explains nothing is deleted, and goes back to the link box', async () => {
    open(on())
    api.routes['POST /api/v1/online/disconnect'] = () => json(off())
    fireEvent.click(await screen.findByRole('button', { name: 'Disconnect' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Nothing on this till is deleted')
    expect(calls('POST', '/api/v1/online/disconnect')).toHaveLength(0)

    fireEvent.click(screen.getAllByRole('button', { name: 'Disconnect' }).at(-1)!)

    expect(await screen.findByLabelText('Connection link')).toBeInTheDocument()
    expect(calls('POST', '/api/v1/online/disconnect')).toHaveLength(1)
  })

  it('keeps the connection when the confirmation is cancelled', async () => {
    open(on())
    fireEvent.click(await screen.findByRole('button', { name: 'Disconnect' }))
    await screen.findByRole('dialog')

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(calls('POST', '/api/v1/online/disconnect')).toHaveLength(0)
    expect(screen.getByText('Corner Shop')).toBeInTheDocument()
  })
})

describe('private Online', () => {
  const GROUPS = ['K7QM', '2XPA', '9DTR', '4WLE', 'H6ZV', '3NCB', '8YFJ', '5GUS', 'T2RD', '7KAM', '4XQP', '9LWE', '6HVB', 'CHKQ']
  const slip = { code: GROUPS.join('-'), groups: GROUPS, keyId: 'ab12cd34ef56ab12' }
  const press = (digits: string) => digits.split('').forEach((d) => fireEvent.click(screen.getByRole('button', { name: d })))
  const wrongPin = () =>
    new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { pin: ['That PIN is not correct.'] }, traceId: 't' }), {
      status: 400,
      headers: { 'Content-Type': 'application/problem+json' },
    })

  it('says the data is locked and shows only the public key id, never the key', async () => {
    open(on())

    expect(await screen.findByText('Privacy')).toBeInTheDocument()
    expect(screen.getByText('Locked')).toBeInTheDocument()
    expect(screen.getByText('ab12cd34ef56ab12')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show recovery code' })).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(calls('POST', '/api/v1/online/sealing')).toHaveLength(0)
  })

  it('asks for the PIN first and shows the code in groups only when the server accepts it', async () => {
    open(on())
    api.routes['POST /api/v1/online/sealing/slip'] = () => json(slip)

    fireEvent.click(await screen.findByRole('button', { name: 'Show recovery code' }))
    expect(screen.queryByText('K7QM')).not.toBeInTheDocument()
    press('1234')

    expect(await screen.findByText('K7QM')).toBeInTheDocument()
    for (const group of GROUPS) expect(screen.getByText(group)).toBeInTheDocument()
    expect(calls('POST', '/api/v1/online/sealing/slip')[0]!.body).toEqual({ pin: '1234' })
    expect(screen.queryByRole('button', { name: 'Start private Online' })).not.toBeInTheDocument() // viewing again is not a confirmation

    fireEvent.click(screen.getByRole('button', { name: 'Done' }))
    await waitFor(() => expect(screen.queryByText('K7QM')).not.toBeInTheDocument())
  })

  it('a wrong PIN shows nothing', async () => {
    open(on())
    api.routes['POST /api/v1/online/sealing/slip'] = () => wrongPin()

    fireEvent.click(await screen.findByRole('button', { name: 'Show recovery code' }))
    press('0000')

    expect(await screen.findByText('Incorrect PIN')).toBeInTheDocument()
    expect(screen.queryByText('K7QM')).not.toBeInTheDocument()
  })

  it('says so in a sentence when too many wrong PINs have locked the check', async () => {
    open(on())
    api.routes['POST /api/v1/online/sealing/slip'] = () =>
      new Response(JSON.stringify({ status: 429, code: 'pin_locked', title: 'Locked', detail: 'Too many wrong PINs. Try again in a few minutes.', traceId: 't' }), {
        status: 429,
        headers: { 'Content-Type': 'application/problem+json' },
      })

    fireEvent.click(await screen.findByRole('button', { name: 'Show recovery code' }))
    press('0000')

    expect(await screen.findByText('Too many wrong PINs. Try again in a few minutes.')).toBeInTheDocument()
  })

  it('prints the slip, or saves it as a PDF, with the PIN again', async () => {
    open(on())
    api.routes['POST /api/v1/online/sealing/slip'] = () => json(slip)
    api.routes['POST /api/v1/online/sealing/slip/print'] = () => json({ printed: true, message: 'Sent to the receipt printer' })
    api.routes['POST /api/v1/online/sealing/slip/pdf'] = () =>
      new Response(new Blob(['%PDF-1.4']), { headers: { 'Content-Type': 'application/pdf', 'Content-Disposition': 'attachment; filename="Recovery slip.pdf"' } })
    const create = vi.fn(() => 'blob:slip')
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: create, revokeObjectURL: vi.fn() }))
    fireEvent.click(await screen.findByRole('button', { name: 'Show recovery code' }))
    press('1234')
    await screen.findByText('K7QM')

    fireEvent.click(screen.getByRole('button', { name: 'Print on receipt paper' }))
    expect(await screen.findByText('Sent to the receipt printer')).toBeInTheDocument()
    expect(calls('POST', '/api/v1/online/sealing/slip/print')[0]!.body).toEqual({ pin: '1234' })

    fireEvent.click(screen.getByRole('button', { name: 'Download PDF' }))
    await waitFor(() => expect(create).toHaveBeenCalled())
    expect(calls('POST', '/api/v1/online/sealing/slip/pdf')[0]!.body).toEqual({ pin: '1234' })
  })

  it('a shop that is not private yet is offered the switch, and nothing starts until the slip is kept', async () => {
    open(on({ sealingState: 'off', sealKeyId: null }))
    api.routes['POST /api/v1/online/sealing/slip'] = () => json(slip)
    api.routes['POST /api/v1/online/sealing/confirm'] = () => json(on())

    expect(await screen.findByText(/whoever runs the cloud could read the copy of your shop/)).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Turn on privacy' }))
    press('1234')
    await screen.findByText('K7QM')

    const start = screen.getByRole('button', { name: 'Start private Online' })
    expect(start).toBeDisabled()
    fireEvent.click(start)
    expect(calls('POST', '/api/v1/online/sealing/confirm')).toHaveLength(0)

    fireEvent.click(screen.getByLabelText(/I printed or saved the recovery code/))
    expect(start).toBeEnabled()
    fireEvent.click(start)

    expect(await screen.findByText('Private Online is on. Your shop is being sent again, locked.')).toBeInTheDocument()
    expect(calls('POST', '/api/v1/online/sealing/confirm')).toHaveLength(1)
    await waitFor(() => expect(screen.queryByText('K7QM')).not.toBeInTheDocument())
    expect(await screen.findByText('Locked')).toBeInTheDocument()
  })

  it('a new connection says nothing is being sent yet and why', async () => {
    open(on({ sealingState: 'needsSlip', sealKeyId: null }))

    expect(await screen.findByText('Not uploading yet')).toBeInTheDocument()
    expect(screen.getByText(/Nothing is sent to the cloud yet/)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show recovery code' })).toBeInTheDocument()
  })

  it('a PC that lost its key offers the recovery code, and says a wrong code or a wrong PIN in their own words', async () => {
    open(on({ sealingState: 'keyMissing', sealKeyId: null, lastErrorCode: 'seal_key_missing' }))
    expect(await screen.findByText(/Uploading is paused because this PC does not have your key/)).toBeInTheDocument()
    expect((await screen.findAllByRole('alert'))[0]).toHaveTextContent('This PC does not have your key, so nothing is being sent.')

    fireEvent.click(screen.getByRole('button', { name: 'Enter recovery code' }))
    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Spaces and dashes do not matter')
    const submit = screen.getByRole('button', { name: 'Restore key' })
    expect(submit).toBeDisabled()

    fireEvent.change(screen.getByLabelText('Recovery code'), { target: { value: 'k7qm 2xpa' } })
    fireEvent.change(screen.getByLabelText('Your PIN'), { target: { value: '1234' } })
    api.routes['POST /api/v1/online/sealing/restore'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { code: ['That is not a complete recovery code. Check it against the slip.'] }, traceId: 't' }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    fireEvent.click(submit)
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('That is not a complete recovery code')
    expect(calls('POST', '/api/v1/online/sealing/restore')[0]!.body).toEqual({ pin: '1234', code: 'k7qm 2xpa' })

    api.routes['POST /api/v1/online/sealing/restore'] = () => wrongPin()
    fireEvent.click(submit)
    await waitFor(() => expect(within(dialog).getByRole('alert')).toHaveTextContent('That PIN is not correct.'))

    api.routes['POST /api/v1/online/sealing/restore'] = () => json(on())
    fireEvent.click(submit)
    expect(await screen.findByText('Key restored')).toBeInTheDocument()
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
  })

  it('making a new key after a loss asks first and says the old cloud copy is lost', async () => {
    open(on({ sealingState: 'keyMissing', sealKeyId: null }))
    fireEvent.click(await screen.findByRole('button', { name: 'Make a new key' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('can never be read again')
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(calls('POST', '/api/v1/online/sealing/slip')).toHaveLength(0)
  })

  it.each([
    ['seal_key', /key could not be opened/],
    ['seal_key_missing', /does not have your key/],
  ])('turns the code %s into advice', async (code, sentence) => {
    open(on({ lastErrorCode: code, sealingState: 'on' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(sentence)
  })

  it('tells a not-yet-connected shop that its data will be locked, before it connects', async () => {
    open(off())

    expect(await screen.findByText(/locked on this PC before it is sent/)).toBeInTheDocument()
  })
})

describe('arabic', () => {
  it('is translated and right-to-left friendly', async () => {
    await setLanguage('ar')
    open(off())

    expect(await screen.findByLabelText('رابط الاتصال')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'اتصال' })).toBeInTheDocument()
    expect(screen.getByLabelText('رابط الاتصال')).toHaveAttribute('dir', 'ltr') // a URL stays left-to-right inside Arabic text
  })
})
