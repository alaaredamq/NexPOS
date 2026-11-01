import type { components } from './schema'
import { getLanguage } from '@/core/i18n/languages'

/** The error body every `api/v1` failure carries (RFC 7807 + `code`); see Web/Api/README.md. */
export type ApiProblemDetails = components['schemas']['ApiProblemDetails']

/** A failed API call. `message` is already localized by the server (`detail ?? title`). */
export class ApiError extends Error {
  readonly status: number
  readonly code: string
  readonly traceId?: string
  /** Only for `validation_failed`: camelCase field name to messages. */
  readonly errors?: Record<string, string[]>

  constructor(status: number, problem: Partial<ApiProblemDetails>, fallbackMessage: string) {
    super(problem.detail || problem.title || fallbackMessage)
    this.name = 'ApiError'
    this.status = status
    this.code = problem.code ?? 'unknown'
    this.traceId = problem.traceId
    this.errors = problem.errors ?? undefined
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError
}

type Listener = (error: ApiError) => void
const listeners = new Set<Listener>()

/**
 * Called for every failed call, before it is thrown to the caller. The shell uses it for the cross-cutting
 * reactions (401 -> back to the login page, `license_sales_stopped` -> refresh the licence banner) so no
 * screen has to remember to.
 */
export function onApiError(listener: Listener): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

export interface RequestOptions {
  query?: Record<string, string | number | boolean | null | undefined>
  /** A `FormData` is sent as multipart (the browser sets the boundary header); anything else is JSON. */
  body?: unknown
  signal?: AbortSignal
  /** Do not run the global listeners (e.g. the boot-time session probe, where 401 is the normal "not signed in"). */
  quiet?: boolean
}

const BASE = '/api/v1'

function buildUrl(path: string, query?: RequestOptions['query']): string {
  const url = `${BASE}${path.startsWith('/') ? path : `/${path}`}`
  if (!query) return url
  const params = new URLSearchParams()
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') params.set(key, String(value))
  }
  const text = params.toString()
  return text ? `${url}?${text}` : url
}

async function send(method: string, path: string, options: RequestOptions): Promise<Response> {
  const headers: Record<string, string> = {
    Accept: 'application/json, application/problem+json',
    'Accept-Language': getLanguage(),
  }
  if (method !== 'GET') headers['X-NexPOS-Client'] = 'web' // the CSRF gate: a cross-site form cannot set a custom header
  const isForm = typeof FormData !== 'undefined' && options.body instanceof FormData
  if (options.body !== undefined && !isForm) headers['Content-Type'] = 'application/json'

  let response: Response
  try {
    response = await fetch(buildUrl(path, options.query), {
      method,
      headers,
      credentials: 'same-origin',
      body: options.body === undefined ? undefined : isForm ? (options.body as FormData) : JSON.stringify(options.body),
      signal: options.signal,
    })
  } catch (cause) {
    if (cause instanceof DOMException && cause.name === 'AbortError') throw cause
    // The server is not reachable (stopped, restarting, wrong port). Not a 5xx: there is no response at all.
    const error = new ApiError(0, { code: 'network_error' }, 'Cannot reach the server.')
    if (!options.quiet) listeners.forEach((l) => l(error))
    throw error
  }

  if (response.ok) return response

  let problem: Partial<ApiProblemDetails> = {}
  try {
    problem = (await response.json()) as Partial<ApiProblemDetails>
  } catch {
    // not JSON (a proxy error page): keep the generic message
  }
  const error = new ApiError(response.status, problem, `Request failed (${response.status}).`)
  if (!options.quiet) listeners.forEach((l) => l(error))
  throw error
}

async function request<T>(method: string, path: string, options: RequestOptions = {}): Promise<T> {
  const response = await send(method, path, options)
  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

/** A file the server generated (CSV, PDF). `filename` comes from `Content-Disposition`; empty when the server sent none. */
export interface Download {
  blob: Blob
  filename: string
}

async function download(method: string, path: string, options: RequestOptions = {}): Promise<Download> {
  const response = await send(method, path, options)
  const disposition = response.headers.get('Content-Disposition') ?? ''
  const encoded = /filename\*=UTF-8''([^;]+)/i.exec(disposition)?.[1]
  const plain = /filename="?([^";]+)"?/i.exec(disposition)?.[1]
  return { blob: await response.blob(), filename: encoded ? decodeURIComponent(encoded) : (plain ?? '') }
}

export const http = {
  get: <T>(path: string, options?: RequestOptions) => request<T>('GET', path, options),
  post: <T>(path: string, body?: unknown, options?: RequestOptions) => request<T>('POST', path, { ...options, body }),
  put: <T>(path: string, body?: unknown, options?: RequestOptions) => request<T>('PUT', path, { ...options, body }),
  patch: <T>(path: string, body?: unknown, options?: RequestOptions) => request<T>('PATCH', path, { ...options, body }),
  delete: <T>(path: string, options?: RequestOptions) => request<T>('DELETE', path, options),
  /** GET a generated file. */
  download: (path: string, options?: RequestOptions) => download('GET', path, options),
  /** POST a JSON body and receive a generated file. */
  downloadPost: (path: string, body: unknown, options?: RequestOptions) => download('POST', path, { ...options, body }),
}
