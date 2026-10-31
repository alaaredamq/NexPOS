// Language state that does NOT depend on i18next, so `api/` can read it for `Accept-Language` without
// pulling the translation library into its import graph.

export const LANGUAGES = ['en', 'ar'] as const
export type Language = (typeof LANGUAGES)[number]
export type Direction = 'ltr' | 'rtl'

export const DEFAULT_LANGUAGE: Language = 'en'
/** Same key the old shell uses for UI prefs (`nexpos.rail`, …). */
export const LANGUAGE_STORAGE_KEY = 'nexpos.lang'

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
