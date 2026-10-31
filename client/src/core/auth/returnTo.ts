/** Where the login page sends the user back to: an in-app path (`/orders?page=2`) carried in `?returnTo=`. */
export function loginUrl(returnTo?: string, reason?: 'idle'): string {
  const params = new URLSearchParams()
  if (returnTo && returnTo !== '/' && !returnTo.startsWith('/login')) params.set('returnTo', returnTo)
  if (reason) params.set('reason', reason)
  const text = params.toString()
  return text ? `/login?${text}` : '/login'
}

/**
 * Accepts only a same-app absolute path. `//evil.com`, `https://evil.com` and `/\evil.com` must never become a redirect
 * target (open-redirect guard), so anything else is dropped and the caller falls back to the landing page.
 */
export function safeReturnTo(value: string | null): string | null {
  if (!value || !value.startsWith('/') || value.startsWith('//') || value.startsWith('/\\')) return null
  if (value.startsWith('/login')) return null
  return value
}
