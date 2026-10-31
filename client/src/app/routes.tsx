import type { RouteObject } from 'react-router-dom'

/**
 * Every `modules/<name>/routes.tsx` default-exports a `RouteObject[]`; they are discovered here so
 * adding a module never touches a shared file (REACT_MIGRATION.md §4.5). Module routes should
 * lazy-load their pages (`lazy: () => import(...)`) to keep the shell inside the D12 budget.
 */
const found = import.meta.glob<{ default: RouteObject[] }>('../modules/*/routes.tsx', { eager: true })

export const moduleRoutes: RouteObject[] = Object.values(found).flatMap((m) => m.default)
