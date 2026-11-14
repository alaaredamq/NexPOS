import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { fakeApi, json, mountModule, stubMatchMedia, type FakeApi } from '../_shared/testApp'

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common')
})

beforeEach(async () => {
  localStorage.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

/** `/app/pair-till` is public: no session is needed (the session probe answers 401, as for any visitor). */
function open(status: object) {
  api = fakeApi()
  api.routes['GET /api/v1/session'] = () => json({ status: 401, title: 'Unauthenticated', code: 'unauthenticated' }, 401)
  api.routes['GET /api/v1/devices/me'] = () => json(status)
  return mountModule('/pair-till', [])
}

describe('pairing screen', () => {
  it('a PC that is not paired asks for the code and trades it for the cookie', async () => {
    open({ isServer: false, access: 'NotRegistered' })
    api.routes['POST /api/v1/devices/pair'] = () => json({ name: 'Back counter' })
    const code = await screen.findByRole('textbox', { name: 'Pairing code' })
    expect(screen.getByRole('button', { name: 'Pair this PC' })).toBeDisabled()

    fireEvent.change(code, { target: { value: ' k7mq-2xwp ' } })
    fireEvent.click(screen.getByRole('button', { name: 'Pair this PC' }))

    expect(await screen.findByText('This PC is now paired as "Back counter". You can sign in and sell.')).toBeInTheDocument()
    expect(api.calls.find((c) => c.method === 'POST')?.body).toEqual({ code: 'k7mq-2xwp' })
  })

  it('a wrong code shows the reason and keeps what was typed', async () => {
    open({ isServer: false, access: 'NotRegistered' })
    api.routes['POST /api/v1/devices/pair'] = () =>
      json({ status: 400, title: 'Bad request', code: 'invalid_pairing_code', detail: 'That pairing code is not valid, has already been used, or has expired.' }, 400)
    const code = await screen.findByRole('textbox', { name: 'Pairing code' })

    fireEvent.change(code, { target: { value: 'AAAA-AAAA' } })
    fireEvent.click(screen.getByRole('button', { name: 'Pair this PC' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('That pairing code is not valid')
    expect(code).toHaveValue('AAAA-AAAA')
  })

  it('the store server is told it never needs pairing', async () => {
    open({ isServer: true, access: 'Allowed' })

    expect(await screen.findByText('This is the store server. It never needs pairing.')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
  })

  it('a till that is already paired says so', async () => {
    open({ isServer: false, access: 'Allowed' })

    expect(await screen.findByText('This PC is already paired and can sell.')).toBeInTheDocument()
  })

  it('a till over the limit gets the over-the-limit explanation above the code box', async () => {
    open({ isServer: false, access: 'SeatsExceeded' })

    await waitFor(() => expect(screen.getByText(/the license now covers fewer PCs than are registered/)).toBeInTheDocument())
  })
})
