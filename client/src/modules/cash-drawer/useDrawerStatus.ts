import { useSession } from '@/core/auth/useSession'

export interface DrawerStatusView {
  /** False when the user cannot use a drawer at all (no `Sales.View`) or the server could not tell. */
  known: boolean
  isOpen: boolean
  sessionId: number | null
  openingCash: number
  openedAt: string | null
}

/**
 * Whether the signed-in user's cash drawer is open: what the shell chip shows and what the till reads before selling.
 *
 * It reads `session.drawer` from the bootstrap payload, which the shell already refetches every 60 s and on window focus,
 * so the chip and every consumer share one request and one answer. This module's open / close / drop mutations also refetch
 * the session, so a change made here shows at once. (A dedicated `GET /cash-drawer/current` exists for the drawer page's own
 * running figures; the till has `GET /pos/drawer`. All three have the same shape.)
 */
export function useDrawerStatus(): DrawerStatusView {
  const { drawer } = useSession()
  if (!drawer) return { known: false, isOpen: false, sessionId: null, openingCash: 0, openedAt: null }
  return {
    known: true,
    isOpen: drawer.isOpen,
    sessionId: drawer.sessionId ?? null,
    openingCash: drawer.openingCash,
    openedAt: drawer.openedAt ?? null,
  }
}
