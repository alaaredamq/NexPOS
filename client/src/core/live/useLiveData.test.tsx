import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { json } from '@/test/sessionFixture'
import { isLiveQuery, LIVE_POLL_MS, useLiveData } from './useLiveData'

afterEach(() => {
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

describe('isLiveQuery', () => {
  const q = (...queryKey: unknown[]) => ({ queryKey }) as never

  it('covers what shows stock and totals, and leaves session, licence and edit forms alone', () => {
    expect(isLiveQuery(q('pos', 'catalog', 'products', 'main'))).toBe(true)
    expect(isLiveQuery(q('products', 'list', {}))).toBe(true)
    expect(isLiveQuery(q('inventory'))).toBe(true)
    expect(isLiveQuery(q('dashboard'))).toBe(true)
    expect(isLiveQuery(q('session'))).toBe(false)
    expect(isLiveQuery(q('license'))).toBe(false)
    expect(isLiveQuery(q('products', 'detail', 7))).toBe(false)
  })
})

describe('another PC sells', () => {
  let version = 'a.0'
  beforeEach(() => {
    version = 'a.0'
    vi.stubGlobal('fetch', vi.fn(async () => json({ version })))
  })

  function setup() {
    const client = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000 } } })
    const invalidate = vi.spyOn(client, 'invalidateQueries')
    const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>
    return { invalidate, wrapper }
  }

  it('reloads the live screens when the data version moves, and not before', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    const { invalidate, wrapper } = setup()
    renderHook(() => useLiveData(), { wrapper })

    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    expect(invalidate).not.toHaveBeenCalled() // the first answer is only the starting point

    await act(() => vi.advanceTimersByTimeAsync(LIVE_POLL_MS))
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(invalidate).not.toHaveBeenCalled() // nothing was sold

    version = 'a.1' // the other till sells
    await act(() => vi.advanceTimersByTimeAsync(LIVE_POLL_MS))
    await waitFor(() => expect(invalidate).toHaveBeenCalledTimes(1))
  })

  it('does nothing until it is enabled (not signed in yet)', async () => {
    const { invalidate, wrapper } = setup()
    renderHook(() => useLiveData(false), { wrapper })
    await new Promise((r) => setTimeout(r, 20))
    expect(fetch).not.toHaveBeenCalled()
    expect(invalidate).not.toHaveBeenCalled()
  })
})
