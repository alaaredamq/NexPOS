import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, http } from './http'

function mockFetch(response: Response) {
  const fetchMock = vi.fn().mockResolvedValue(response)
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

afterEach(() => vi.unstubAllGlobals())

describe('http body types', () => {
  it('sends a FormData as multipart without forcing a JSON content type', async () => {
    const fetchMock = mockFetch(new Response(JSON.stringify({ ok: true }), { status: 200 }))
    const form = new FormData()
    form.set('apply', 'true')

    await http.post('/products/import', form)

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.body).toBe(form)
    expect((init.headers as Record<string, string>)['Content-Type']).toBeUndefined()
    expect((init.headers as Record<string, string>)['X-NexPOS-Client']).toBe('web')
  })

  it('still sends plain objects as JSON', async () => {
    const fetchMock = mockFetch(new Response('{}', { status: 200 }))

    await http.post('/x', { a: 1 })

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.body).toBe('{"a":1}')
    expect((init.headers as Record<string, string>)['Content-Type']).toBe('application/json')
  })
})

describe('http.download', () => {
  it('returns the blob and the file name from Content-Disposition', async () => {
    mockFetch(new Response('a,b', { status: 200, headers: { 'Content-Disposition': 'attachment; filename=products_20261031.csv; filename*=UTF-8\'\'products_20261031.csv' } }))

    const file = await http.download('/products/export')

    expect(file.filename).toBe('products_20261031.csv')
    expect(file.blob.size).toBe(3)
  })

  it('decodes an RFC 5987 file name and tolerates a missing header', async () => {
    mockFetch(new Response('x', { headers: { 'Content-Disposition': "attachment; filename*=UTF-8''%D9%85%D9%86%D8%AA%D8%AC%D8%A7%D8%AA.csv" } }))
    expect((await http.download('/x')).filename).toBe('منتجات.csv')

    mockFetch(new Response('x'))
    expect((await http.download('/x')).filename).toBe('')
  })

  it('posts a JSON body for downloadPost and throws the problem for a failure', async () => {
    const fetchMock = mockFetch(
      new Response(JSON.stringify({ code: 'validation_failed', title: 'Please correct the highlighted fields.' }), { status: 400 }),
    )

    await expect(http.downloadPost('/products/sticker-pdf', { code: '1' })).rejects.toBeInstanceOf(ApiError)

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.method).toBe('POST')
    expect(init.body).toBe('{"code":"1"}')
  })
})
