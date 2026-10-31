import type { ReactNode } from 'react'
import { Navigate, Outlet } from 'react-router-dom'
import type { Permission } from '@/api/permissions'
import { useSession } from '@/core/auth/useSession'

type Required = Permission | readonly Permission[]

/** Pure check, usable outside React (stores, tests). Like the server: an exact key match, any one of several is enough. */
export function hasPermission(held: readonly string[], required: Required | undefined): boolean {
  if (required === undefined) return true
  const list = typeof required === 'string' ? [required] : required
  return list.length === 0 || list.some((key) => held.includes(key))
}

/** `useCan(Permissions.Orders_View)`; several keys mean "any one of". Hiding a button is not security - the API check is the real one. */
export function useCan(required?: Required): boolean {
  return hasPermission(useSession().permissions, required)
}

interface CanProps {
  permission: Required
  /** Rendered instead when the user lacks it (default: nothing). */
  fallback?: ReactNode
  children: ReactNode
}

export function Can({ permission, fallback = null, children }: CanProps) {
  return useCan(permission) ? <>{children}</> : <>{fallback}</>
}

/**
 * Route guard: `{ element: <RequirePermission permission={Permissions.Orders_View} />, children: [...] }`.
 * A typed-in URL, a stale bookmark or a role edit mid-session all land here, so the route re-checks instead of
 * trusting that the rail was the only way in. The API refuses the calls independently.
 */
export function RequirePermission({ permission }: { permission: Required }) {
  return useCan(permission) ? <Outlet /> : <Navigate to="/forbidden" replace />
}
