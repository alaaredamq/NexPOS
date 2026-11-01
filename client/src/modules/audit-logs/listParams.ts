import { CATEGORIES, OUTCOMES, SEVERITIES, type AuditParams } from './api'

export const PAGE_SIZES = [25, 50, 100, 200]
export const DEFAULT_PAGE_SIZE = 50

export const EMPTY_FILTERS: Pick<AuditParams, 'q' | 'from' | 'to' | 'user' | 'category' | 'outcome' | 'severity' | 'entityType' | 'entityId' | 'action' | 'request'> = {
  q: '',
  from: '',
  to: '',
  user: '',
  category: '',
  outcome: '',
  severity: '',
  entityType: '',
  entityId: '',
  action: '',
  request: '',
}

const isLocalDateTime = (v: string | null): v is string => v !== null && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(v)
const oneOf = (list: readonly string[], v: string | null) => (v !== null && list.includes(v) ? v : '')

/** The page's state lives in the URL, so a refresh, the back button and a link from support land on the same rows. Unknown values fall back to "no filter". */
export function readParams(search: URLSearchParams): AuditParams {
  const page = Number(search.get('page'))
  const size = Number(search.get('size'))
  const record = search.get('record')
  const entry = Number(search.get('entry'))
  const user = search.get('user') ?? ''
  return {
    page: Number.isInteger(page) && page > 0 ? page : 1,
    pageSize: PAGE_SIZES.includes(size) ? size : DEFAULT_PAGE_SIZE,
    dir: search.get('dir') === 'asc' ? 'asc' : 'desc',
    q: search.get('q') ?? '',
    from: isLocalDateTime(search.get('from')) ? (search.get('from') as string) : '',
    to: isLocalDateTime(search.get('to')) ? (search.get('to') as string) : '',
    user: /^\d+$/.test(user) || /^name:.+/.test(user) ? user : '',
    category: oneOf(CATEGORIES, search.get('category')),
    outcome: oneOf(OUTCOMES, search.get('outcome')),
    severity: oneOf(SEVERITIES, search.get('severity')),
    entityType: search.get('type') ?? '',
    entityId: record && /^\d+$/.test(record) ? record : '',
    action: search.get('action') ?? '',
    request: search.get('request') ?? '',
    entry: Number.isInteger(entry) && entry > 0 ? entry : null,
  }
}

/** Only what differs from the defaults goes into the URL. */
export function writeParams(p: AuditParams): URLSearchParams {
  const out = new URLSearchParams()
  if (p.page > 1) out.set('page', String(p.page))
  if (p.pageSize !== DEFAULT_PAGE_SIZE) out.set('size', String(p.pageSize))
  if (p.dir === 'asc') out.set('dir', 'asc')
  if (p.q) out.set('q', p.q)
  if (p.from) out.set('from', p.from)
  if (p.to) out.set('to', p.to)
  if (p.user) out.set('user', p.user)
  if (p.category) out.set('category', p.category)
  if (p.outcome) out.set('outcome', p.outcome)
  if (p.severity) out.set('severity', p.severity)
  if (p.entityType) out.set('type', p.entityType)
  if (p.entityId) out.set('record', p.entityId)
  if (p.action) out.set('action', p.action)
  if (p.request) out.set('request', p.request)
  if (p.entry) out.set('entry', String(p.entry))
  return out
}

/** Is any filter set? (The page size, sort and the open entry are not filters.) */
export const hasFilters = (p: AuditParams) => Object.keys(EMPTY_FILTERS).some((key) => p[key as keyof typeof EMPTY_FILTERS] !== '')
