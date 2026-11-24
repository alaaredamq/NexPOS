import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import '@/core/i18n'
import { loadNamespaces, setLanguage } from '@/core/i18n'
import { Permissions } from '@/api/permissions'
import { toast } from '@/ui/toast'
import { fakeApi, json, mountModule, sessionWith, stubMatchMedia, type FakeApi } from '../_shared/testApp'
import { moved } from './api'
import routes from './routes'

const linked = { connected: true, linked: true, menuUrl: 'https://cloud.test/m/beit', editorUrl: 'https://cloud.test/owner/menu.html', lastSentUtc: null }

const menu = (overrides = {}) => ({
  sections: [
    { id: 1, name: 'Breakfast', productCount: 13, entryCount: 2 },
    { id: 2, name: 'Grills', productCount: 0, entryCount: 0 },
  ],
  productsShown: 13,
  productsLeftOut: 5,
  status: linked,
  ...overrides,
})

let api: FakeApi

beforeAll(async () => {
  stubMatchMedia()
  await loadNamespaces('common', 'onlineMenu')
})

beforeEach(async () => {
  localStorage.clear()
  toast.clear()
  await setLanguage('en')
})
afterEach(() => vi.unstubAllGlobals())

function open(body = menu(), ...permissions: string[]) {
  api = fakeApi(sessionWith(...(permissions.length ? permissions : [Permissions.Products_Edit])))
  api.routes['GET /api/v1/online-menu'] = () => json(body)
  api.routes['GET /api/v1/online-menu/sections/1/entries'] = () =>
    json({
      items: [
        { id: 11, kind: 'category', categoryId: 4, name: 'Manakish', productCount: 12, isActive: true },
        { id: 12, kind: 'product', productId: 40, name: 'Turkish coffee', price: 1.75, currencyNotation: '$', productCount: 1, isActive: true },
      ],
      hasMore: false,
    })
  return mountModule('/online-menu', routes)
}

describe('online menu page', () => {
  it('shows the sections, the first one opened, and the live address', async () => {
    open()

    expect(await screen.findByRole('heading', { name: 'Breakfast' })).toBeInTheDocument()
    expect(await screen.findByText('Whole category · 12 products')).toBeInTheDocument()
    expect(screen.getByText('Turkish coffee')).toBeInTheDocument()
    expect(screen.getByText('https://cloud.test/m/beit')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Add pictures and descriptions' })).toHaveAttribute('href', 'https://cloud.test/owner/menu.html')
  })

  it('needs Products.Edit', async () => {
    const router = open(menu(), Permissions.Products_View)

    await waitFor(() => expect(router.state.location.pathname).toBe('/forbidden'))
  })

  it('with no sections explains that the menu follows the categories', async () => {
    open(menu({ sections: [], productsLeftOut: 0 }))

    expect(await screen.findByText('No sections yet')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add the first section' })).toBeInTheDocument()
  })

  it('tells a till that is not connected where to connect', async () => {
    open(menu({ status: { connected: false, linked: null } }))

    expect(await screen.findByText('This till is not connected to Coduo POS Online')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Open Settings > Online' })).toHaveAttribute('href', expect.stringMatching(/\/settings\/online$/))
  })

  it('a pick moves one step on the server, so a half-loaded list is fine', async () => {
    open()
    api.routes['POST /api/v1/online-menu/entries/11/move'] = () => new Response(null, { status: 204 })
    await screen.findByText('Turkish coffee')
    const detail = screen.getByRole('region', { name: 'Breakfast' })

    fireEvent.click(within(detail).getAllByRole('button', { name: 'Move down' })[0]!)

    await waitFor(() => expect(api.calls.some((c) => c.url.endsWith('/entries/11/move'))).toBe(true))
    expect(api.calls.find((c) => c.url.endsWith('/entries/11/move'))?.body).toEqual({ direction: 1 })
  })

  it('moving a section down sends the new order', async () => {
    open()
    api.routes['PUT /api/v1/online-menu/sections/order'] = () => new Response(null, { status: 204 })
    const list = await screen.findByRole('navigation', { name: 'Sections' })

    fireEvent.click(within(list).getAllByRole('button', { name: 'Move down' })[0]!)

    await waitFor(() => expect(api.calls.some((c) => c.method === 'PUT' && c.url.endsWith('/sections/order'))).toBe(true))
    expect(api.calls.find((c) => c.url.endsWith('/sections/order'))?.body).toEqual({ ids: [2, 1] })
  })

  it('adding a whole category posts it to the open section', async () => {
    open()
    api.routes['GET /api/v1/online-menu/categories'] = () =>
      json([
        { id: 4, name: 'Manakish', activeProducts: 12, sectionId: 1, sectionName: 'Breakfast' },
        { id: 5, name: 'Drinks', activeProducts: 7, sectionId: null, sectionName: null },
      ])
    api.routes['POST /api/v1/online-menu/sections/1/entries'] = () => new Response(null, { status: 204 })
    await screen.findByRole('heading', { name: 'Breakfast' })

    fireEvent.click(screen.getAllByRole('button', { name: 'Add products' })[0]!)
    const dialog = await screen.findByRole('dialog')
    expect(await within(dialog).findByText('Already here')).toBeInTheDocument()
    fireEvent.click(await within(dialog).findByRole('checkbox', { name: /Drinks/ }))
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add 1' }))

    await waitFor(() => expect(api.calls.some((c) => c.method === 'POST' && c.url.endsWith('/sections/1/entries'))).toBe(true))
    expect(api.calls.find((c) => c.url.endsWith('/sections/1/entries'))?.body).toEqual({ productIds: [], categoryIds: [5] })
  })
})

describe('moved', () => {
  it('swaps with the neighbour and ignores moves past either end', () => {
    expect(moved([1, 2, 3], 0, 1)).toEqual([2, 1, 3])
    expect(moved([1, 2, 3], 2, -1)).toEqual([1, 3, 2])
    expect(moved([1, 2, 3], 0, -1)).toEqual([1, 2, 3])
    expect(moved([1, 2, 3], 2, 1)).toEqual([1, 2, 3])
  })
})
