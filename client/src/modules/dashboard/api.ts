import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import type { Period } from './trend'

type Schemas = components['schemas']
export type Dashboard = Schemas['DashboardDto']
export type DashboardOrder = Schemas['DashboardOrderDto']
export type DashboardCategory = Schemas['DashboardCategoryDto']
export type SalesTrend = Schemas['SalesTrendDto']

export const dashboardKeys = {
  all: ['dashboard'] as const,
  overview: ['dashboard', 'overview'] as const,
  trend: (period: Period) => ['dashboard', 'trend', period] as const,
}

// The server caches these figures for two minutes; refetching on focus or every minute is cheap and keeps a screen left open honest.
const REFRESH_MS = 60_000

export function useDashboard() {
  return useQuery({
    queryKey: dashboardKeys.overview,
    queryFn: ({ signal }) => http.get<Dashboard>('/dashboard', { signal }),
    refetchInterval: REFRESH_MS,
  })
}

export function useSalesTrend(period: Period) {
  return useQuery({
    queryKey: dashboardKeys.trend(period),
    queryFn: ({ signal }) => http.get<SalesTrend>('/dashboard/sales-trend', { query: { period }, signal }),
    placeholderData: keepPreviousData,
    refetchInterval: REFRESH_MS,
  })
}
