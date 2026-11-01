import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { json } from '@/test/sessionFixture'
import { usePosProducts } from './api'

afterEach(() => vi.unstubAllGlobals())

describe('the till always shows the stock the server has now', () => {
  it('asks for the tiles again every time the till is opened, even within the app-wide 30 s freshness', async () => {
    let calls = 0
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => {
        calls++
        return json({ items: [], total: 0, page: 1, pageSize: 120, isMainScreen: true })
      }),
    )
    // the app's own defaults: data is fresh for 30 s, and a returning screen would normally reuse it
    const client = new QueryClient({ defaultOptions: { queries: { staleTime: 30_000 } } })
    const wrapper = ({ children }: { children: ReactNode }) => <QueryClientProvider client={client}>{children}</QueryClientProvider>

    const first = renderHook(() => usePosProducts(null), { wrapper })
    await waitFor(() => expect(first.result.current.isSuccess).toBe(true))
    expect(calls).toBe(1)
    first.unmount()

    // back from the Inventory page a few seconds later: stock may have changed there
    const second = renderHook(() => usePosProducts(null), { wrapper })
    await waitFor(() => expect(calls).toBe(2))
    second.unmount()
  })
})
