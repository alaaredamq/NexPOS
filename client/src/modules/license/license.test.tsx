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
