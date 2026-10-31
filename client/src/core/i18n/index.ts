// i18n entry point (section 0E). Import once, at startup, for the side effect: `import '@/core/i18n'`.
//
// Conventions (also in docs/react-migration/notes/0E.md):
//  - One namespace per `locales/<lang>/<ns>.json`, loaded lazily with the route that needs it.
//  - Keys are the .resx names verbatim and may contain dots/colons ("Signing in..."), so BOTH separators are
//    disabled. A namespace is chosen with the hook — `useTranslation('orders')` — never with `t('orders:Key')`.
//  - Placeholders are i18next's `{{0}}` / `{{name}}`; the exporter rewrites the old `{0}` / `{name}` mechanically:
//    `t('PackOf', { 0: 12 })`.
//  - No plural forms: the old app has none ("{0} minutes"); phrase with a number placeholder instead.

import i18next, { type BackendModule } from 'i18next'
import { initReactI18next } from 'react-i18next'
import {
  DEFAULT_LANGUAGE,
  LANGUAGES,
  applyDocumentLanguage,
  getLanguage,
  isLanguage,
  readStoredLanguage,
  writeStoredLanguage,
  type Language,
} from './languages'

export * from './languages'
export * from './format'
export { useLanguage } from './useLanguage'
export { useTranslation } from 'react-i18next'

type Bundle = Record<string, string>

// Vite turns every locales/<lang>/<ns>.json into its own chunk; a module's namespace only downloads
// when its route renders (or when `loadNamespaces` is called from a route loader).
const bundles = import.meta.glob<{ default: Bundle }>('../../locales/*/*.json')

const lazyJsonBackend: BackendModule = {
  type: 'backend',
  init() {},
  read(language, namespace, callback) {
    const load = bundles[`../../locales/${language}/${namespace}.json`]
    if (!load) return callback(null, {}) // a namespace that exists in one language only is not an error
    load().then(
      (module) => callback(null, module.default),
      (error: unknown) => callback(error instanceof Error ? error : new Error(String(error)), null),
    )
  },
}

export const DEFAULT_NAMESPACE = 'common'

export const i18n = i18next.createInstance()

void i18n
  .use(lazyJsonBackend)
  .use(initReactI18next)
  .init({
    lng: getLanguage(),
    fallbackLng: DEFAULT_LANGUAGE,
    supportedLngs: [...LANGUAGES],
    defaultNS: DEFAULT_NAMESPACE,
    ns: [DEFAULT_NAMESPACE],
    keySeparator: false,
    nsSeparator: false,
    interpolation: { escapeValue: false }, // React escapes already
    returnEmptyString: false,
    react: { useSuspense: true },
    saveMissing: import.meta.env.DEV,
    missingKeyHandler: (languages, namespace, key) => {
      if (import.meta.env.DEV) console.warn(`[i18n] missing key "${key}" in ${namespace} (${languages.join(', ')})`)
    },
  })

applyDocumentLanguage(getLanguage())
i18n.on('languageChanged', (language) => {
  if (isLanguage(language)) applyDocumentLanguage(language)
})

/** Switch language: persists to `localStorage['nexpos.lang']`, flips `<html lang dir>`, re-renders subscribers. */
export async function setLanguage(language: Language): Promise<void> {
  writeStoredLanguage(language)
  await i18n.changeLanguage(language)
}

/**
 * First-visit seed: when nothing is stored yet, adopt the server's language (the session bootstrap knows the
 * .AspNetCore.Culture cookie, which is HttpOnly and unreadable here). A stored choice always wins.
 */
export async function seedLanguage(serverLanguage: string | null | undefined): Promise<void> {
  if (readStoredLanguage() || !isLanguage(serverLanguage)) return
  await setLanguage(serverLanguage)
}

/** Start downloading namespaces ahead of render — call from a route `loader` to avoid a Suspense waterfall. */
export function loadNamespaces(...namespaces: string[]): Promise<void> {
  return i18n.loadNamespaces(namespaces)
}
