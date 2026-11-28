// The admin API (/api/admin/v1, docs/online/admin-api.md): typed shapes and one fetch wrapper. The same API the phone admin app uses,
// so every rule lives on the server. Signing in gives a token (nxa_...), kept on this device until it expires or you sign out.

const BASE = '/api/admin/v1'
const TOKEN_KEY = 'coduo.console.token'

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message) }
}

export const auth = {
  get token(): string | null { try { return localStorage.getItem(TOKEN_KEY) } catch { return null } },
  set(token: string | null) {
    try { if (token) localStorage.setItem(TOKEN_KEY, token); else localStorage.removeItem(TOKEN_KEY) } catch { /* private mode */ }
  },
}

/** Called when the cloud stops accepting the token (expired, or signed out elsewhere). */
let onUnauthorized: () => void = () => { }
export const setUnauthorizedHandler = (f: () => void) => { onUnauthorized = f }
/** Called after every change the cloud accepted, so every screen shows it. */
let onChanged: () => void = () => { }
export const setChangedHandler = (f: () => void) => { onChanged = f }

type Query = Record<string, string | number | null | undefined>

async function send<T>(method: string, path: string, body?: unknown, query?: Query, raw?: { body: BodyInit; type: string }): Promise<T> {
  const q = new URLSearchParams()
  for (const [k, v] of Object.entries(query ?? {})) if (v !== null && v !== undefined && v !== '') q.set(k, String(v))
  const headers: Record<string, string> = { Accept: 'application/json' }
  const token = auth.token
  if (token) headers.Authorization = 'Bearer ' + token
  let payload: BodyInit | undefined
  if (raw) { headers['Content-Type'] = raw.type; payload = raw.body }
  else if (body !== undefined) { headers['Content-Type'] = 'application/json'; payload = JSON.stringify(body) }
  let res: Response
  try {
    res = await fetch(BASE + path + (q.size ? '?' + q : ''), { method, headers, body: payload })
  } catch {
    throw new ApiError(0, 'offline', 'Could not reach the cloud. Check the connection and try again.')
  }
  const text = await res.text()
  if (res.ok) {
    if (method !== 'GET' && !path.startsWith('/auth/')) onChanged()
    return (text ? JSON.parse(text) : null) as T
  }
  let code = 'error', message = `The cloud answered ${res.status}.`
  try { const e = JSON.parse(text); code = e.code ?? code; message = e.message ?? message } catch { /* not JSON */ }
  if (res.status === 401 && path !== '/auth/login') onUnauthorized()
  throw new ApiError(res.status, code, message)
}

const get = <T,>(path: string, query?: Query) => send<T>('GET', path, undefined, query)
const post = <T,>(path: string, body?: unknown) => send<T>('POST', path, body ?? {})
const put = <T,>(path: string, body?: unknown) => send<T>('PUT', path, body ?? {})
const del = (path: string) => send<null>('DELETE', path)

// ---------------------------------------------------------------- shapes

export type ClientType = 'online' | 'pos' | 'menu'
export type PlanState = 'None' | 'Active' | 'Grace' | 'Expired' | 'Suspended'
export interface PulseCell { hourUtc: string; contacts: number }
export interface Attention { severity: 'warn' | 'bad'; title: string; reason: string; action: string; portalPath?: string | null; clientId?: number | null }

export interface Me { username: string; deviceName?: string; currency: string; timeZone: string; today: string }

export interface Overview {
  summary: string; clients: number; connected: number
  plans: { active: number; endingSoon: number; ended: number }
  productsHeld: number; ordersHeld: number; pulse: PulseCell[]; litHours: number; attention: Attention[]
  thisMonth: { collected: number; spent: number; profit: number; balance: number }
}

export interface ClientRow {
  id: number; name: string; isActive: boolean; type: ClientType; phone?: string | null; email?: string | null; branches: number
  subscription?: { state: PlanState; expiresAtUtc?: string | null; suspended: boolean; endingSoon: boolean; note: string } | null
  lastSeenAtUtc?: string | null; seen: string; tags: string[]; products?: number | null; orders?: number | null; pulse: PulseCell[]
  sub?: string
}

export interface Subscription { state: PlanState; startsAtUtc?: string | null; expiresAtUtc?: string | null; suspended: boolean; graceEndsAtUtc?: string | null }

export interface Site {
  id: string; clientId: number; name: string; machineId?: string | null; isDisabled: boolean
  connection: 'NotLinked' | 'WaitingForShop' | 'LinkExpired' | 'Connected' | 'Disconnected'
  connectionLabel: string; connectionHelp: string; health: 'Never' | 'Ok' | 'Stale' | 'Down'
  lastSeenAtUtc?: string | null; lastSyncAtUtc?: string | null; resyncRequested: boolean
  shop?: { plan?: string | null; licenseKind?: string | null; licenseUntil?: string | null; seats?: number | null; schemaVersion?: string | null } | null
  pulse: PulseCell[]
}

export interface SiteDetail {
  site: Site
  data: { held: boolean; encrypted: boolean; counts: Record<string, number> }
  tokens: { prefix: string; createdAtUtc?: string; lastUsedAtUtc?: string | null; revokedAtUtc?: string | null }[]
  openLink?: { expiresAtUtc: string } | null
  syncLog: { receivedAtUtc: string; changes: number; outcome: string; error?: string | null; initial?: boolean; durationMs?: number }[]
}

export interface OwnerLogin { id: number; signIn: string; displayName?: string | null; role: 'owner' | 'manager' | 'staff'; isActive: boolean; createdAtUtc?: string; lastSignInAtUtc?: string | null }

export interface Payment {
  id: number; clientId?: number | null; payer: string; amount: number; date: string; method: string
  purpose: string; purposes: string[]; reference?: string | null; note?: string | null; createdBy?: string | null
}

export interface ClientDetail {
  id: number; name: string; phone?: string | null; email?: string | null; notes?: string | null; isActive: boolean; createdAtUtc?: string
  type: ClientType; hasOnline: boolean; hasPos: boolean; menuLive: boolean; menuPublished?: boolean; hasMenu?: boolean
  subscription?: Subscription | null; subscriptionState: PlanState
  sites: Site[]; logins: OwnerLogin[]
  payments: { count: number; total: number; last12Months: number; last?: Payment | null }
  attention: Attention[]
}

export interface LinkResult { url: string; code?: string | null; expiresAtUtc?: string | null; whatsAppUrl?: string | null }
export interface NewClientResult { client: ClientDetail; siteId?: string | null; link?: { url: string; expiresAtUtc: string } | null }
export interface LoginHandOver { login: OwnerLogin; password: string; message?: string | null; whatsAppUrl?: string | null }
export interface ClientEvent { id: number; atUtc: string; text: string }

export interface FinanceOptions { currency: string; methods: string[]; purposes: string[]; expenseCategories: string[]; maxAmount: number; today: string }
export interface BudgetUse { planned: number; actual: number; left: number; percent: number; over: boolean }
export interface Budget { month: string; incomeTarget?: number | null; spendingLimit?: number | null }
export interface ShareRow { name: string; amount: number; count: number; clientId?: number | null }
export interface FinanceLine { type: 'payment' | 'expense'; id: number; date: string; who: string; clientId?: number | null; details: string; note?: string | null; amount: number }
export interface Renewal { clientId: number; name: string; what?: string; endsAtUtc?: string | null; lastPaid?: number | null; licenseId?: number | null }
export interface FinanceSummary {
  period: { kind: 'month' | 'year'; label: string; from: string; to: string; query: string; previous?: string | null; next?: string | null }
  currency: string; collected: number; spent: number; profit: number; marginPercent?: number | null; balance: number
  previous?: { collected: number; spent: number; profit: number }
  change?: { collected?: string | null; spent?: string | null } | null
  payingClients: number
  budget?: { monthBudget?: Budget | null; income?: BudgetUse | null; spending?: BudgetUse | null } | null
  trend: { month: string; collected: number; spent: number; profit: number }[]
  averages?: { collected: number; spent: number; profit: number }
  byPayer: ShareRow[]; byPurpose: ShareRow[]; byCategory: ShareRow[]
  renewals: Renewal[]; renewalsExpected: number; attention: Attention[]; lines: FinanceLine[]
}

export interface Licence {
  id: number; clientId?: number | null; clientName?: string | null; siteId?: string | null; machineId: string; customer: string
  kind: string; plan: string; seats: number; issuedOn?: string | null; validUntil?: string | null; timeLimited: boolean
  status: 'Active' | 'EndingSoon' | 'Grace' | 'Expired' | 'Updates' | 'UpdatesEnding' | 'UpdatesEnded' | 'Superseded'
  statusLabel: string; serial: string; key?: string | null; source: string; paymentId?: number | null; note?: string | null
}
export interface Signer { state: string; ready: boolean; message: string }

export type MenuState = 'none' | 'draft' | 'published'
export interface MenuRow {
  clientId: number; clientName: string; isActive: boolean; type: ClientType; live: boolean; state: MenuState; editorUrl: string
  menu?: {
    title: string; slug: string; url: string; previewUrl: string; sections: number; items: number; hidden: number; noPicture: number
    fromPos: boolean; currency: string; updatedAtUtc: string
  } | null
}
/** A dish as the file gave it, all as text (what was typed), so the review can show and fix it. typePrices: order type id -> price. */
export interface MenuImportRow {
  row: number; section: string; name: string; description: string; price: string
  typePrices: Record<string, string>; hidden: string; soldOut: string
}
export interface MenuImportItem extends MenuImportRow {
  status: 'add' | 'update' | 'same' | 'skip'; message?: string | null; fromPos: boolean
  priceText?: string | null; typePriceTexts: { type: string; price: string }[]
}
export interface MenuImportResult {
  applied: boolean; rows: number; added: number; updated: number; unchanged: number; skipped: number
  newSections: string[]; issues: { row: number; message: string; skipped: boolean }[]; moreIssues: boolean
  currency: string; sections: string[]; orderTypes: { id: number; name: string }[]; items: MenuImportItem[]
}

/** A file the API hands out behind the token: fetched, then given to the browser as a download. */
async function download(path: string, fallbackName: string) {
  const res = await fetch(BASE + path, { headers: { Authorization: 'Bearer ' + (auth.token ?? '') } })
  if (res.status === 401) { onUnauthorized(); throw new ApiError(401, 'unauthorized', 'Please sign in again.') }
  if (!res.ok) throw new ApiError(res.status, 'download_failed', 'The download did not work. Try again.')
  const named = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(res.headers.get('Content-Disposition') ?? '')
  const name = named ? decodeURIComponent(named[1] ?? named[2]) : fallbackName
  const a = Object.assign(document.createElement('a'), { href: URL.createObjectURL(await res.blob()), download: name })
  a.click()
  setTimeout(() => URL.revokeObjectURL(a.href), 2000)
}

const XLSX = 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet'

// ---------------------------------------------------------------- calls

export const api = {
  login: (username: string, password: string) =>
    send<{ token: string; expiresAtUtc: string; username: string }>('POST', '/auth/login', { username, password, deviceName: 'Console (web)' }),
  logout: () => post<null>('/auth/logout'),
  me: () => get<Me>('/me'),

  overview: () => get<Overview>('/overview'),
  clients: () => get<ClientRow[]>('/clients'),
  createClient: (b: { name: string; phone?: string; email?: string; notes?: string; type: ClientType; months: number }) => post<NewClientResult>('/clients', b),
  client: (id: number) => get<ClientDetail>(`/clients/${id}`),
  updateClient: (id: number, b: { name: string; phone?: string | null; email?: string | null; notes?: string | null }) => put<ClientDetail>(`/clients/${id}`, b),
  setActive: (id: number, on: boolean) => post(`/clients/${id}/${on ? 'activate' : 'deactivate'}`),
  removeClient: (id: number) => del(`/clients/${id}`),
  setOnline: (id: number, on: boolean) => post<{ link?: LinkResult | null }>(`/clients/${id}/online/${on ? 'enable' : 'disable'}`),
  addPos: (id: number) => post<{ link?: LinkResult | null }>(`/clients/${id}/pos/enable`),
  events: (id: number) => get<ClientEvent[]>(`/clients/${id}/events`, { take: 30 }),
  extend: (id: number, months: number) => post<Subscription>(`/clients/${id}/subscription/extend`, { months }),
  suspend: (id: number, on: boolean) => post<Subscription>(`/clients/${id}/subscription/${on ? 'suspend' : 'resume'}`),

  addSite: (clientId: number, name: string, machineId?: string) => post<SiteDetail>(`/clients/${clientId}/sites`, { name, machineId: machineId || null }),
  site: (id: string) => get<SiteDetail>(`/sites/${id}`),
  updateSite: (id: string, name: string, machineId?: string | null) => put<SiteDetail>(`/sites/${id}`, { name, machineId: machineId || null }),
  removeSite: (id: string) => del(`/sites/${id}`),
  newLink: (id: string) => post<LinkResult>(`/sites/${id}/link`),
  disconnect: (id: string) => post<SiteDetail>(`/sites/${id}/disconnect`),
  issueToken: (id: string) => post<{ token: string }>(`/sites/${id}/token`),
  resync: (id: string) => post<SiteDetail>(`/sites/${id}/resync`),
  setSiteDisabled: (id: string, off: boolean) => post<SiteDetail>(`/sites/${id}/${off ? 'disable' : 'enable'}`),

  logins: (clientId: number) => get<OwnerLogin[]>(`/clients/${clientId}/logins`),
  addLogin: (clientId: number, b: { signIn: string; displayName?: string; password?: string; role: string }) => post<LoginHandOver>(`/clients/${clientId}/logins`, b),
  updateLogin: (clientId: number, loginId: number, b: { role?: string; isActive?: boolean }) => put<OwnerLogin>(`/clients/${clientId}/logins/${loginId}`, b),
  resetPassword: (clientId: number, loginId: number) => post<LoginHandOver>(`/clients/${clientId}/logins/${loginId}/reset-password`),
  removeLogin: (clientId: number, loginId: number) => del(`/clients/${clientId}/logins/${loginId}`),

  financeOptions: () => get<FinanceOptions>('/finance/options'),
  financeSummary: (period?: string) => {
    const [k, v] = (period ?? '').split('=')
    return get<FinanceSummary>('/finance/summary', k && v ? { [k]: v } : undefined)
  },
  clientPayments: (clientId: number) => get<Payment[]>(`/clients/${clientId}/payments`),
  savePayment: (b: { clientId?: number | null; payer?: string | null; amount: number; date: string; method: string; purposes: string[]; reference?: string | null; note?: string | null }) =>
    post<Payment>('/finance/payments', b),
  deletePayment: (id: number) => del(`/finance/payments/${id}`),
  saveExpense: (b: { amount: number; date: string; category: string; payee?: string | null; note?: string | null }) => post('/finance/expenses', b),
  deleteExpense: (id: number) => del(`/finance/expenses/${id}`),
  setBudget: (month: string, b: { incomeTarget: number | null; spendingLimit: number | null }) => put(`/finance/budgets/${month}`, b),
  /** The CSV export needs the token, so it is fetched and handed to the browser as a download. */
  exportCsv: async (period: string) => {
    const [k, v] = period.split('=')
    const res = await fetch(`${BASE}/finance/export?${k}=${v}`, { headers: { Authorization: 'Bearer ' + (auth.token ?? '') } })
    if (!res.ok) throw new ApiError(res.status, 'export_failed', 'The export did not work. Try again.')
    const blob = await res.blob()
    const a = Object.assign(document.createElement('a'), { href: URL.createObjectURL(blob), download: `coduo-finance-${v}.csv` })
    a.click()
    setTimeout(() => URL.revokeObjectURL(a.href), 2000)
  },

  licences: (show: string) => get<Licence[]>('/licenses', { show }),
  clientLicences: (clientId: number) => get<Licence[]>(`/clients/${clientId}/licenses`),
  licence: (id: number) => get<Licence>(`/licenses/${id}`),
  signer: () => get<Signer>('/licenses/signer'),
  issueLicence: (b: Record<string, unknown>) => post<{ license: Licence; whatsAppUrl?: string | null }>('/licenses/issue', b),
  recordLicence: (key: string, clientId?: number | null) => post<Licence>('/licenses/record', { key, clientId: clientId ?? null }),
  importLicences: (csv: string) => send<{ added: number; alreadyKnown: number; linked: number; problems: string[] }>('POST', '/licenses/import', undefined, undefined, { body: csv, type: 'text/csv' }),
  linkLicence: (id: number, clientId: number | null, note?: string | null) => put<Licence>(`/licenses/${id}`, { clientId, note: note ?? null }),
  deleteLicence: (id: number) => del(`/licenses/${id}`),

  menus: () => get<MenuRow[]>('/menus'),
  menuTemplate: (clientId: number) => download(`/clients/${clientId}/menu/template`, 'menu-items.xlsx'),
  /** Reads and checks the file: every row comes back with what would happen to it. Nothing is saved. */
  checkMenuFile: (clientId: number, file: File) =>
    send<MenuImportResult>('POST', `/clients/${clientId}/menu/import`, undefined, undefined, { body: file, type: XLSX }),
  /** The reviewed rows, checked again (`apply` false) or saved once confirmed (`apply` true). */
  importMenuRows: (clientId: number, rows: MenuImportRow[], apply: boolean) =>
    send<MenuImportResult>('POST', `/clients/${clientId}/menu/import/rows`, { rows }, { apply: apply ? 'true' : null }),
}
