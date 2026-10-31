import { useEffect, useRef } from 'react'

/**
 * Minutes without a key press, pointer move or touch before the session is locked. 30 matches the server's own
 * session idle limit (`AddSession` in Program.cs). The Razor app had no client-side lock - its cookie simply lives
 * 8 hours - so this is new behaviour required by the plan (G0); change the number here if the shop wants it shorter.
 */
export const IDLE_TIMEOUT_MS = 30 * 60 * 1000

const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart'] as const
const CHECK_EVERY_MS = 5_000

/**
 * Calls `onIdle` once after `timeoutMs` without activity. Checked on a timer *and* when the tab becomes visible
 * again, so a PC that slept past the limit locks the moment someone touches it rather than at the next tick.
 * Scanner input arrives as key presses, so a cashier scanning is never idle.
 */
export function useIdleTimeout(onIdle: () => void, timeoutMs: number = IDLE_TIMEOUT_MS): void {
  const lastActive = useRef(0)
  const handler = useRef(onIdle)
  useEffect(() => {
    handler.current = onIdle
  })

  useEffect(() => {
    lastActive.current = Date.now()
    let fired = false

    const markActive = () => {
      lastActive.current = Date.now()
    }
    const check = () => {
      if (fired || Date.now() - lastActive.current < timeoutMs) return
      fired = true
      handler.current()
    }
    const onVisible = () => {
      if (document.visibilityState === 'visible') check()
    }

    ACTIVITY_EVENTS.forEach((name) => window.addEventListener(name, markActive, { passive: true }))
    document.addEventListener('visibilitychange', onVisible)
    const timer = window.setInterval(check, Math.min(CHECK_EVERY_MS, timeoutMs))
    return () => {
      ACTIVITY_EVENTS.forEach((name) => window.removeEventListener(name, markActive))
      document.removeEventListener('visibilitychange', onVisible)
      window.clearInterval(timer)
    }
  }, [timeoutMs])
}
