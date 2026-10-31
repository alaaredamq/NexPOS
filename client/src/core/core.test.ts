import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { hasPermission } from '@/core/permissions/permissions'
import { loginUrl, safeReturnTo } from '@/core/auth/returnTo'
import { IDLE_TIMEOUT_MS, useIdleTimeout } from '@/core/idle/useIdleTimeout'
import { findCurrent, flattenNav, landingPath, toRouterPath } from '@/shell/nav'
import { makeSession, navItem, section } from '@/test/sessionFixture'

describe('hasPermission', () => {
  it('is an exact match and any-of', () => {
    expect(hasPermission(['Orders.View'], 'Orders.View' as never)).toBe(true)
    expect(hasPermission(['Orders.View'], 'Orders.Edit' as never)).toBe(false)
    expect(hasPermission(['Orders.View'], ['Orders.Edit', 'Orders.View'] as never)).toBe(true)
  })
  it('an empty requirement means every signed-in user', () => {
    expect(hasPermission([], undefined)).toBe(true)
    expect(hasPermission([], [] as never)).toBe(true)
  })
})

describe('returnTo', () => {
  it('keeps same-app paths and drops anything that could leave the app', () => {
    expect(safeReturnTo('/orders?page=2')).toBe('/orders?page=2')
    expect(safeReturnTo('//evil.example')).toBeNull()
    expect(safeReturnTo('https://evil.example')).toBeNull()
    expect(safeReturnTo(String.raw`/\evil.example`)).toBeNull()
    expect(safeReturnTo('/login')).toBeNull()
    expect(safeReturnTo(null)).toBeNull()
  })
  it('builds the login url', () => {
    expect(loginUrl()).toBe('/login')
    expect(loginUrl('/')).toBe('/login')
    expect(loginUrl('/orders?page=2')).toBe('/login?returnTo=%2Forders%3Fpage%3D2')
    expect(loginUrl(undefined, 'idle')).toBe('/login?reason=idle')
  })
})

describe('nav helpers', () => {
  it('strips the router basename', () => {
    expect(toRouterPath('/app/cash-drawer')).toBe('/cash-drawer')
    expect(toRouterPath('/app')).toBe('/')
  })
  it('longest match wins and respects segment boundaries', () => {
    const items = flattenNav([section('Sales', [navItem('POS'), navItem('PosDisplay', { spaPath: '/app/pos-display' })])])
    expect(findCurrent(items, '/pos-display')?.key).toBe('PosDisplay')
    expect(findCurrent(items, '/pos/checkout')?.key).toBe('POS')
    expect(findCurrent(items, '/other')).toBeUndefined()
  })
  it('lands on the POS when the role has it, else the first page, else nowhere', () => {
    expect(landingPath(makeSession())).toBe('/pos')
    expect(landingPath(makeSession({ nav: [section('Sales', [navItem('Orders')])] }))).toBe('/orders')
    expect(landingPath(makeSession({ nav: [] }))).toBeNull()
  })
})

describe('useIdleTimeout', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('fires once after the timeout and activity postpones it', () => {
    const onIdle = vi.fn()
    renderHook(() => useIdleTimeout(onIdle, 60_000))

    act(() => vi.advanceTimersByTime(50_000))
    act(() => {
      window.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }))
    })
    act(() => vi.advanceTimersByTime(50_000))
    expect(onIdle).not.toHaveBeenCalled() // 100 s passed, but only 50 s since the key press

    act(() => vi.advanceTimersByTime(20_000))
    expect(onIdle).toHaveBeenCalledTimes(1)
    act(() => vi.advanceTimersByTime(300_000))
    expect(onIdle).toHaveBeenCalledTimes(1)
  })

  it('defaults to the 30 minute limit', () => {
    expect(IDLE_TIMEOUT_MS).toBe(30 * 60 * 1000)
  })
})
