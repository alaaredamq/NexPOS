import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import routes from './routes'

const store = (overrides = {}) => ({
  storeName: 'Corner Shop',
  addressLine1: '1 Main St',
  addressLine2: 'Beirut',
  phone: '01 234 567',
  taxId: 'TX-1',
  website: 'shop.example.com',
  receiptFooterMessage: 'Thanks!',
  footerLine1: 'No refunds',
  footerLine2: '',
  footerLine3: '',
  ...overrides,
})

const printing = (overrides = {}) => ({ receiptPrinterName: '', stickerWidthMm: 40, stickerHeightMm: 25, ...overrides })

const branding = (overrides = {}) => ({
  appName: '',
  defaultAppName: 'NexPos',
  hasCustomLogo: false,
  hasCustomFavicon: false,
  logoUrl: '/branding/logo?v=1',
  faviconUrl: '/branding/favicon?v=1',
  maxNameLength: 40,
  maxFileBytes: 2 * 1024 * 1024,
  logoExtensions: ['.png', '.jpg', '.jpeg', '.svg', '.webp'],
  faviconExtensions: ['.png', '.ico', '.svg'],
  ...overrides,
})

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  window.URL.createObjectURL = vi.fn(() => 'blob:preview')
  window.URL.revokeObjectURL = vi.fn()
  await loadNamespaces('common', 'settings')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(path: string, ...permissions: string[]) {
  api = fakeApi(sessionWith(...permissions))
  api.routes['GET /api/v1/settings/store'] = () => json(store())
  api.routes['GET /api/v1/settings/printing'] = () => json(printing())
  api.routes['GET /api/v1/settings/printers'] = () => json({ items: ['XP-80C', 'Back office'] })
  api.routes['GET /api/v1/settings/branding'] = () => json(branding())
  return mountModule(path, routes)
}

const calls = (method: string, url: string) => api.calls.filter((c) => c.method === method && c.url.startsWith(url))

describe('access', () => {
  it('/settings goes to the store tab', async () => {
    const router = open('/settings', Permissions.Settings_Manage)

    await waitFor(() => expect(router.state.location.pathname).toBe('/settings/store'))
    expect(await screen.findByLabelText(/Store Name/)).toHaveValue('Corner Shop')
  })

  it.each(['/settings/store', '/settings/printers', '/settings/branding'])('%s is forbidden with Settings.View alone', async (path) => {
    const router = open(path, Permissions.Settings_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })
})

describe('store settings', () => {
  it('fills the form and follows what is typed in the receipt preview', async () => {
    open('/settings/store', Permissions.Settings_Manage)
    const name = await screen.findByLabelText(/Store Name/)
    const preview = screen.getByRole('img', { name: 'Receipt Preview' })
    expect(within(preview).getByText('Corner Shop')).toBeInTheDocument()
    expect(within(preview).getByText(/^Phone: .*01 234 567/)).toBeInTheDocument()

    fireEvent.change(name, { target: { value: 'New Name' } })
    fireEvent.change(screen.getByLabelText(/Footer Message/), { target: { value: 'come again' } })

    await waitFor(() => expect(within(preview).getByText('New Name')).toBeInTheDocument())
    expect(within(preview).getByText('come again')).toBeInTheDocument()
  })

  it('does not save a blank name and says why', async () => {
    open('/settings/store', Permissions.Settings_Manage)
    fireEvent.change(await screen.findByLabelText(/Store Name/), { target: { value: '   ' } })

    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    expect(await screen.findByText('The store name is required.')).toBeInTheDocument()
    expect(calls('PUT', '/api/v1/settings/store')).toHaveLength(0)
  })

  it('saves the edited fields and confirms', async () => {
    open('/settings/store', Permissions.Settings_Manage)
    api.routes['PUT /api/v1/settings/store'] = (init) => json({ ...store(), ...JSON.parse(String(init?.body)) })
    fireEvent.change(await screen.findByLabelText(/Footer Line 2/), { target: { value: 'Open 9 to 5' } })

    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    expect(await screen.findByText('Settings saved successfully')).toBeInTheDocument()
    expect(calls('PUT', '/api/v1/settings/store')[0]!.body).toMatchObject({ storeName: 'Corner Shop', footerLine2: 'Open 9 to 5', taxId: 'TX-1' })
  })

  it('puts a field error from the server on the field', async () => {
    open('/settings/store', Permissions.Settings_Manage)
    api.routes['PUT /api/v1/settings/store'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { phone: ['Phone is too long.'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    fireEvent.change(await screen.findByLabelText(/^Phone/), { target: { value: '123' } })

    fireEvent.click(screen.getByRole('button', { name: 'Save Changes' }))

    expect(await screen.findByText('Phone is too long.')).toBeInTheDocument()
  })

  it('resets to the factory text only after confirming', async () => {
    open('/settings/store', Permissions.Settings_Manage)
    api.routes['POST /api/v1/settings/store/reset'] = () => json(store({ storeName: 'Your Store Name', addressLine1: '' }))
    await screen.findByLabelText(/Store Name/)

    fireEvent.click(screen.getByRole('button', { name: 'Reset to Defaults' }))
    const dialog = await screen.findByRole('dialog')
    expect(calls('POST', '/api/v1/settings/store/reset')).toHaveLength(0)
    fireEvent.click(within(dialog).getByRole('button', { name: 'Reset to Defaults' }))

    await waitFor(() => expect(screen.getByLabelText(/Store Name/)).toHaveValue('Your Store Name'))
    expect(screen.getByLabelText(/Address Line 1/)).toHaveValue('')
  })
})

describe('printers and labels', () => {
  it('offers the installed printers and keeps a configured one that is not detected', async () => {
    open('/settings/printers', Permissions.Settings_Manage)
    api.routes['GET /api/v1/settings/printing'] = () => json(printing({ receiptPrinterName: 'Old printer' }))

    const select = await screen.findByRole('combobox', { name: /^Receipt Printer/ })
    await waitFor(() => expect(select).toHaveValue('Old printer (not detected)'))
    fireEvent.click(select)

    expect(await screen.findByRole('option', { name: 'XP-80C' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Back office' })).toBeInTheDocument()
  })

  it('asks for a printer before a test print, then shows the answer of the server', async () => {
    open('/settings/printers', Permissions.Settings_Manage)
    api.routes['POST /api/v1/settings/printers/test'] = () => json({ printed: false, message: "Could not print to 'XP-80C'." })
    await screen.findByRole('combobox', { name: /^Receipt Printer/ })

    fireEvent.click(screen.getByRole('button', { name: 'Test' }))
    expect(await screen.findByText('Select a printer first')).toBeInTheDocument()
    expect(calls('POST', '/api/v1/settings/printers/test')).toHaveLength(0)

    fireEvent.click(screen.getByRole('combobox', { name: /^Receipt Printer/ }))
    fireEvent.click(await screen.findByRole('option', { name: 'XP-80C' }))
    fireEvent.click(screen.getByRole('button', { name: 'Test' }))

    expect(await screen.findByText("Could not print to 'XP-80C'.")).toBeInTheDocument()
    expect(calls('POST', '/api/v1/settings/printers/test')[0]!.body).toEqual({ printerName: 'XP-80C' })
  })

  it('saves the printer and the sticker size', async () => {
    open('/settings/printers', Permissions.Settings_Manage)
    api.routes['PUT /api/v1/settings/printing'] = (init) => json(JSON.parse(String(init?.body)))
    fireEvent.click(await screen.findByRole('combobox', { name: /^Receipt Printer/ }))
    fireEvent.click(await screen.findByRole('option', { name: 'Back office' }))
    fireEvent.change(screen.getByLabelText(/Sticker Width/), { target: { value: '50' } })

    fireEvent.click(screen.getByRole('button', { name: 'Save Settings' }))

    expect(await screen.findByText('Printer settings saved')).toBeInTheDocument()
    expect(calls('PUT', '/api/v1/settings/printing')[0]!.body).toEqual({ receiptPrinterName: 'Back office', stickerWidthMm: 50, stickerHeightMm: 25 })
  })

  it('refuses a sticker size outside the limits without calling the API', async () => {
    open('/settings/printers', Permissions.Settings_Manage)
    fireEvent.change(await screen.findByLabelText(/Sticker Width/), { target: { value: '5' } })
    fireEvent.change(screen.getByLabelText(/Sticker Height/), { target: { value: '300' } })

    fireEvent.click(screen.getByRole('button', { name: 'Save Settings' }))

    expect(await screen.findByText('Enter a width from 15 to 150 mm.')).toBeInTheDocument()
    expect(screen.getByText('Enter a height from 10 to 150 mm.')).toBeInTheDocument()
    expect(calls('PUT', '/api/v1/settings/printing')).toHaveLength(0)
  })

  it('the sticker printer of this computer is only available in the desktop app', async () => {
    open('/settings/printers', Permissions.Settings_Manage)

    expect(await screen.findByRole('combobox', { name: /^Sticker Printer/ })).toBeDisabled()
    expect(screen.getByText(/Only available in the installed desktop app/)).toBeInTheDocument()
  })
})

describe('branding', () => {
  const png = (name = 'logo.png') => new File([new Uint8Array(10)], name, { type: 'image/png' })

  it('shows the defaults and a placeholder with the default name', async () => {
    open('/settings/branding', Permissions.Settings_Manage)

    const name = await screen.findByLabelText('App name')
    expect(name).toHaveValue('')
    expect(name).toHaveAttribute('placeholder', 'NexPos')
    expect(screen.queryByLabelText('Remove and use the default logo')).not.toBeInTheDocument()
  })

  it('rejects a file of the wrong type or size on the spot, and nothing is sent', async () => {
    open('/settings/branding', Permissions.Settings_Manage)
    const favicon = await screen.findByLabelText('Favicon')

    fireEvent.change(favicon, { target: { files: [png('icon.gif')] } })
    expect(await screen.findByText(/Unsupported file type '\.gif'/)).toBeInTheDocument()

    const big = new File([new Uint8Array(3 * 1024 * 1024)], 'logo.png', { type: 'image/png' })
    fireEvent.change(screen.getByLabelText('Logo'), { target: { files: [big] } })
    expect(await screen.findByText('The file is too large (max 2 MB).')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeDisabled()
  })

  it('sends the name and the picked logo as a form, then refreshes the session the shell reads', async () => {
    open('/settings/branding', Permissions.Settings_Manage)
    let sent: FormData | undefined
    api.routes['PUT /api/v1/settings/branding'] = (init) => {
      sent = init?.body as FormData
      return json(branding({ appName: 'Acme POS', hasCustomLogo: true }))
    }
    fireEvent.change(await screen.findByLabelText('App name'), { target: { value: 'Acme POS' } })
    fireEvent.change(screen.getByLabelText('Logo'), { target: { files: [png()] } })
    const sessionCalls = calls('GET', '/api/v1/session').length

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Branding saved')).toBeInTheDocument()
    expect(sent!.get('appName')).toBe('Acme POS')
    expect((sent!.get('logo') as File).name).toBe('logo.png')
    expect(sent!.get('removeLogo')).toBe('false')
    await waitFor(() => expect(calls('GET', '/api/v1/session').length).toBeGreaterThan(sessionCalls))
    expect(await screen.findByLabelText('Remove and use the default logo')).toBeInTheDocument()
  })

  it('removes a custom logo with the checkbox', async () => {
    open('/settings/branding', Permissions.Settings_Manage)
    api.routes['GET /api/v1/settings/branding'] = () => json(branding({ hasCustomLogo: true }))
    let sent: FormData | undefined
    api.routes['PUT /api/v1/settings/branding'] = (init) => {
      sent = init?.body as FormData
      return json(branding())
    }
    fireEvent.click(await screen.findByLabelText('Remove and use the default logo'))

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('Branding saved')).toBeInTheDocument()
    expect(sent!.get('removeLogo')).toBe('true')
    expect(sent!.get('logo')).toBeNull()
  })

  it('shows the server message of a refused file under that file', async () => {
    open('/settings/branding', Permissions.Settings_Manage)
    api.routes['PUT /api/v1/settings/branding'] = () =>
      new Response(JSON.stringify({ status: 400, code: 'validation_failed', title: 'Invalid', errors: { logo: ['That file is not an image.'] } }), {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' },
      })
    fireEvent.change(await screen.findByLabelText('Logo'), { target: { files: [png()] } })

    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    expect(await screen.findByText('That file is not an image.')).toBeInTheDocument()
  })
})

describe('Arabic', () => {
  it('lays the store page out in Arabic', async () => {
    await setLanguage('ar')
    await loadNamespaces('common', 'settings')
    open('/settings/store', Permissions.Settings_Manage)

    expect(await screen.findByText('معلومات المتجر')).toBeInTheDocument()
    await setLanguage('en')
  })
})
