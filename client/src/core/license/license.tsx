import { Navigate, Outlet } from 'react-router-dom'
import type { LicenseSummary } from '@/api/types'
import { useSession } from '@/core/auth/useSession'

/** The licence as the shell last saw it (refreshed with the session every 60 s and on `license_sales_stopped`). */
export function useLicense(): LicenseSummary {
  return useSession().license
}

/** The shop runs the Pro plan (everything). Basic keeps product stock counts but leaves out the Inventory page, suppliers, purchases, the audit log and reports beyond a week. */
export function useIsPro(): boolean {
  return useLicense().plan === 'Pro'
}

/** Basic's reports and dashboard chart cover at most this many days (`PlanRules.BasicReportDays` on the server). */
export const BASIC_REPORT_DAYS = 7

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
