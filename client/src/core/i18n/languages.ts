// Language state that does NOT depend on i18next, so `api/` can read it for `Accept-Language` without
// pulling the translation library into its import graph.

export const LANGUAGES = ['en', 'ar'] as const
export type Language = (typeof LANGUAGES)[number]
export type Direction = 'ltr' | 'rtl'

export const DEFAULT_LANGUAGE: Language = 'en'
/** Same key the old shell uses for UI prefs (`nexpos.rail`, …). */
export const LANGUAGE_STORAGE_KEY = 'nexpos.lang'
/**
 * Same name, as a cookie: the one place the old Razor pages (language in the URL) and the SPA (language in localStorage) agree.
 * Razor pages write it from their URL's language (LanguageMiddleware); the SPA writes it on every change and reads it once at start,
 * so choosing Arabic on the old till and opening the dashboard in the SPA (or the other way round) keeps the language.
 */
export const LANGUAGE_COOKIE = 'nexpos.lang'

export function isLanguage(value: unknown): value is Language {
  return typeof value === 'string' && (LANGUAGES as readonly string[]).includes(value)
}

export function directionOf(language: Language): Direction {
  return language === 'ar' ? 'rtl' : 'ltr'
}

/** The language saved by a previous visit, or `null` on a first visit / unreadable storage. */
export function readStoredLanguage(): Language | null {
  try {
    const stored = localStorage.getItem(LANGUAGE_STORAGE_KEY)
    return isLanguage(stored) ? stored : null
  } catch {
    return null // storage blocked (private mode, policy): behave like a first visit
  }
}

export function writeStoredLanguage(language: Language): void {
  try {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, language)
  } catch {
    // not persisted; the language still applies for this page load
  }
  try {
    document.cookie = `${LANGUAGE_COOKIE}=${language}; path=/; max-age=31536000; samesite=lax`
  } catch {
    // cookies blocked: the Razor pages simply keep their own language
  }
}

/** The language the Razor pages last showed (or the SPA last chose), if the cookie is there. */
function readLanguageCookie(): Language | null {
  try {
    const match = new RegExp(`(?:^|; )${LANGUAGE_COOKIE}=([^;]*)`).exec(document.cookie)
    return match && isLanguage(match[1]) ? match[1] : null
  } catch {
    return null
  }
}

/** Call once before the first render: a language chosen on a Razor page wins over the one this browser stored earlier. */
export function syncLanguageFromCookie(): void {
  const fromCookie = readLanguageCookie()
  if (fromCookie && fromCookie !== readStoredLanguage()) writeStoredLanguage(fromCookie)
}

/** Current UI language, synchronously. What the HTTP client sends as `Accept-Language`. */
export function getLanguage(): Language {
  return readStoredLanguage() ?? DEFAULT_LANGUAGE
}

/** Mirror the language onto `<html lang dir>` — RTL layout is pure CSS logical properties (D8). */
export function applyDocumentLanguage(language: Language): void {
  const root = document.documentElement
  root.lang = language
  root.dir = directionOf(language)
}
