import { useEffect } from 'react'
import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { getLanguage, seedLanguage } from '@/core/i18n'
import { IDLE_TIMEOUT_MS, useIdleTimeout } from '@/core/idle/useIdleTimeout'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { StatusCard } from '@/shell/pages/StatusCard'
import { loginUrl } from './returnTo'
import { useRefreshSession, useSessionQuery } from './useSession'
import { useSignOut } from './useSignOut'

const CHANGE_PIN_PATH = '/account/change-pin'

/**
 * Everything behind the sign-in. Boots from `GET /api/v1/session`: while it loads the page is a spinner, a 401 sends the
 * user to `/login?returnTo=<where they were>`, and only a successful bootstrap renders the routes below - which is why
 * `useSession()` never has to handle "no session".
 */
export function RequireAuth() {
  const { t } = useTranslation()
  const location = useLocation()
  const { data, error, isPending, refetch, isFetching } = useSessionQuery()
  const signOut = useSignOut()
  const refreshSession = useRefreshSession()

  // The very first visit adopts the server's language (its culture cookie is HttpOnly); a stored choice always wins.
  const serverLanguage = data?.language
  useEffect(() => {
    const before = getLanguage()
    void seedLanguage(serverLanguage).then(() => {
      // Nav labels come back in the language of the request, so they have to be fetched again after the switch.
      if (getLanguage() !== before) void refreshSession()
    })
  }, [serverLanguage, refreshSession])

  // An admin set this PIN: until the user has chosen their own, every page but the change-PIN screen sends them there (track A1).
  const mustChangePin = data?.user.mustChangePin === true

  useIdleTimeout(() => void signOut({ reason: 'idle' }), IDLE_TIMEOUT_MS)

  if (isApiError(error) && error.status === 401) {
    return <Navigate to={loginUrl(`${location.pathname}${location.search}`)} replace />
  }

  if (isPending) return <Loader fullSection size="lg" />
  if (mustChangePin && location.pathname !== CHANGE_PIN_PATH) return <Navigate to={CHANGE_PIN_PATH} replace />

  if (!data) {
    // The server did not answer (stopped, restarting). Not a sign-out: keep the URL and let them retry.
    return (
      <StatusCard
        glyph="plug"
        title={t('Shell_ServerDown')}
        actions={
          <Button variant="primary" loading={isFetching} onClick={() => void refetch()}>
            {t('common.retry', 'Retry')}
          </Button>
        }
      >
        {error?.message ?? t('Shell_ServerDownBody')}
      </StatusCard>
    )
  }

  return <Outlet />
}
