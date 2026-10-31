import { Navigate, Outlet } from 'react-router-dom'
import type { LicenseSummary } from '@/api/types'
import { useSession } from '@/core/auth/useSession'

/** The licence as the shell last saw it (refreshed with the session every 60 s and on `license_sales_stopped`). */
export function useLicense(): LicenseSummary {
  return useSession().license
}

/** Mirrors the Razor banner: only when the licence needs attention (renewal due, or selling stopped). */
export function needsAttention(license: LicenseSummary): boolean {
  return license.showsWarning || !license.allowsSales
}

/**
 * Route guard for everything that sells (POS and its checkout): while the licence stops sales the route shows the
 * licence-stopped page instead. Reads, reports, backup and the licence screens stay open (0A, `[RequireSalesLicense]`).
 */
export function RequireSalesLicense() {
  return useLicense().allowsSales ? <Outlet /> : <Navigate to="/license-stopped" replace />
}
