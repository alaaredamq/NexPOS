import { keepPreviousData, useQuery } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http, type Download } from '@/api/http'

type Schemas = components['schemas']
export type AuditEntry = Schemas['AuditLogDto']
export type AuditEntryList = Schemas['AuditLogDtoPagedResult']
export type AuditEntryDetails = Schemas['AuditLogDetailsDto']
export type AuditStats = Schemas['AuditLogStatsDto']
export type AuditOptions = Schemas['AuditLogOptionsDto']

export const CATEGORIES = ['Data', 'Request', 'Security', 'Business', 'System', 'Diagnostic'] as const
/** The sections of the product an entry can belong to, in the order the page lists them (same names as the API). */
export const AREAS = ['Sales', 'Pos', 'Inventory', 'Customers', 'Account', 'Settings', 'Reports', 'System', 'Other'] as const
export const OUTCOMES = ['Success', 'Failure', 'Denied'] as const
/** The severities worth filtering on: the filter means "this bad or worse", so Info would be "everything". */
export const SEVERITIES = ['Warning', 'Error', 'Critical'] as const

/**
 * The state of the page, as it lives in the URL. Dates are the local `yyyy-MM-ddTHH:mm` of the pickers; they become UTC
 * instants only on the way to the API (see {@link toQuery}).
 */
export interface AuditParams {
  page: number
  pageSize: number
  /** Newest first (`desc`) unless the user flipped it or drilled into one request. */
  dir: 'asc' | 'desc'
  q: string
  from: string
  to: string
  /** `''`, a user id (`'7'`) or `name:<username>` for actors without an account (`System`, a failed sign-in). */
  user: string
  /** `''` (every section) or one of {@link AREAS}. */
  area: string
  category: string
  outcome: string
  severity: string
  entityType: string
  entityId: string
  action: string
  /** Request id: shows everything one click wrote. */
  request: string
  /** The entry open in the details drawer. */
  entry: number | null
}

const localToIso = (local: string, addSeconds = 0): string | undefined => {
  if (!local) return undefined
  const date = new Date(local)
  if (Number.isNaN(date.getTime())) return undefined
  return new Date(date.getTime() + addSeconds * 1000).toISOString()
}

/** The API's filters for these params. `to` covers the whole minute that was picked ("to 14:30" includes 14:30:59). */
export function toQuery(p: AuditParams): Record<string, string | number | undefined> {
  const named = p.user.startsWith('name:')
  return {
    q: p.q || undefined,
    dir: p.dir,
    from: localToIso(p.from),
    to: localToIso(p.to, 60),
    userId: p.user && !named ? Number(p.user) : undefined,
    username: named ? p.user.slice(5) : undefined,
    area: p.area || undefined,
    category: p.category || undefined,
    outcome: p.outcome || undefined,
    minSeverity: p.severity || undefined,
    entityType: p.entityType || undefined,
    entityId: p.entityId ? Number(p.entityId) : undefined,
    action: p.action || undefined,
    correlationId: p.request || undefined,
  }
}

const root = ['audit-logs'] as const
export const auditKeys = {
  all: root,
  list: (query: object) => [...root, 'list', query] as const,
  summary: [...root, 'summary'] as const,
  options: [...root, 'options'] as const,
  entry: (id: number) => [...root, 'entry', id] as const,
}

export function useAuditEntries(params: AuditParams) {
  const query = { ...toQuery(params), page: params.page, pageSize: params.pageSize }
  return useQuery({
    queryKey: auditKeys.list(query),
    queryFn: ({ signal }) => http.get<AuditEntryList>('/audit-logs', { query, signal }),
    placeholderData: keepPreviousData,
  })
}

export function useAuditSummary() {
  return useQuery({ queryKey: auditKeys.summary, queryFn: ({ signal }) => http.get<AuditStats>('/audit-logs/summary', { signal }) })
}

/** The drop-down contents. They change rarely (the server caches them for a minute), so a stale copy is fine for a while. */
export function useAuditOptions() {
  return useQuery({
    queryKey: auditKeys.options,
    queryFn: ({ signal }) => http.get<AuditOptions>('/audit-logs/options', { signal }),
    staleTime: 60_000,
  })
}

export function useAuditEntry(id: number | null) {
  return useQuery({
    queryKey: auditKeys.entry(id ?? 0),
    queryFn: ({ signal }) => http.get<AuditEntryDetails>(`/audit-logs/${id}`, { signal }),
    enabled: id !== null,
    // An entry never changes once written.
    staleTime: Infinity,
  })
}

export const auditApi = {
  export: (params: AuditParams): Promise<Download> => http.download('/audit-logs/export', { query: toQuery(params) }),
}
