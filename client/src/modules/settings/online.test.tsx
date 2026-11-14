import { fireEvent, screen, waitFor } from '@testing-library/react'
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
    expect(await screen.findByText('Connected to Coduo POS Online')).toBeInTheDocument()
    expect(screen.queryByLabelText('Connection link')).not.toBeInTheDocument()
  })

  it('does not call the server for an empty box', async () => {
    open(off())

    fireEvent.click(await screen.findByRole('button', { name: 'Connect' }))

    expect(await screen.findByText(/doesn't look like a Coduo POS Online link/)).toBeInTheDocument()
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

describe('recovery code', () => {
  const CODE = 'DLNE-EURX-WYBS-ECX5-TSMP-KPIZ-WQPA-3Y4Y-JS2J-4UYN-KN4P-LPPW-GIC7-JAXS'

  it('is not fetched or shown until the owner asks, then shown and hidden again', async () => {
    open(on())
    api.routes['GET /api/v1/online/recovery-code'] = () => json({ code: CODE })
    await screen.findByText('Corner Shop')
    expect(calls('GET', '/api/v1/online/recovery-code')).toHaveLength(0)
    expect(screen.queryByText(CODE)).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Show recovery code' }))

    expect(await screen.findByTestId('recovery-code')).toHaveTextContent(CODE)
    expect(screen.getByText(/If this PC is lost/)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'I have saved it, hide' }))
    expect(screen.queryByTestId('recovery-code')).not.toBeInTheDocument()
  })

  it('says so when the till has no code to give', async () => {
    open(on())
    api.routes['GET /api/v1/online/recovery-code'] = () => problem(404, 'no_recovery_code')
    await screen.findByText('Corner Shop')

    fireEvent.click(screen.getByRole('button', { name: 'Show recovery code' }))

    await waitFor(() => expect(screen.getByRole('button', { name: 'Show recovery code' })).toBeInTheDocument())
    expect(screen.queryByTestId('recovery-code')).not.toBeInTheDocument()
  })

  it('is not offered before the till is connected', async () => {
    open(off())
    await screen.findByLabelText('Connection link')

    expect(screen.queryByRole('button', { name: 'Show recovery code' })).not.toBeInTheDocument()
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
