// Pure helpers behind `npm run i18n:export` and `npm run i18n:lint` (no filesystem writes here).
// Sources of truth stay in the old app: Web/Resources/**/*.resx and Web/wwwroot/js/locales/{en,ar}.json.

import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'

export const LANGUAGES = ['en', 'ar']
export const SHARED_NAMESPACE = 'common'

const XML_ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" }

function decodeXml(text) {
  return text.replace(/&(#x[0-9a-fA-F]+|#\d+|[a-zA-Z]+);/g, (whole, body) => {
    if (body[0] === '#') {
      const code = body[1] === 'x' ? parseInt(body.slice(2), 16) : parseInt(body.slice(1), 10)
      return String.fromCodePoint(code)
    }
    return XML_ENTITIES[body] ?? whole
  })
}

/** `<data name="K"><value>V</value></data>` → Map(K → V). Binary/typed resources (`type=`/`mimetype=`) are skipped. */
export function parseResx(xml) {
  const entries = new Map()
  const dataTag = /<data\s+([^>]*?)(\/>|>([\s\S]*?)<\/data>)/g
  for (const match of xml.matchAll(dataTag)) {
    const attrs = match[1] ?? ''
    if (/\b(type|mimetype)\s*=/.test(attrs)) continue
    const name = /\bname\s*=\s*"([^"]*)"/.exec(attrs)?.[1]
    if (name === undefined) continue
    const body = match[3] ?? ''
    const value = /<value>([\s\S]*?)<\/value>/.exec(body)?.[1] ?? ''
    const cdata = /^<!\[CDATA\[([\s\S]*)\]\]>$/.exec(value)
    entries.set(decodeXml(name), cdata ? cdata[1] : decodeXml(value))
  }
  return entries
}

/** `POS` → `pos`, `CashDrawer` → `cashDrawer`, `ProductDisplayOrder` → `productDisplayOrder`, `_Layout` → `layout`. */
export function namespaceFromFolder(folder) {
  const name = folder.replace(/^_+/, '')
  if (name === 'Shared') return 'layout' // Views/Shared holds the layout resources
  const caps = /^[A-Z]+/.exec(name)?.[0] ?? ''
  if (caps.length <= 1) return name.charAt(0).toLowerCase() + name.slice(1)
  // An acronym run: lowercase it, but keep the capital that starts the next word ("DBBackup" → "dbBackup").
  const nextIsWord = /[a-z]/.test(name.charAt(caps.length))
  const cut = nextIsWord ? caps.length - 1 : caps.length
  return name.slice(0, cut).toLowerCase() + name.slice(cut)
}

/** Flatten nested JSON sections to `a.b.c` keys (the old `window.i18n.t('pos.cartIsEmpty')` paths). */
export function flatten(object, prefix = '') {
  const out = {}
  for (const [key, value] of Object.entries(object)) {
    const path = prefix ? `${prefix}.${key}` : key
    if (value !== null && typeof value === 'object') Object.assign(out, flatten(value, path))
    else out[path] = String(value)
  }
  return out
}

function walk(dir) {
  const out = []
  for (const name of readdirSync(dir)) {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) out.push(...walk(full))
    else out.push(full)
  }
  return out
}

/**
 * Reads every source into `{ [lang]: { [namespace]: { key: value } } }`.
 *
 * Several views of one folder share a namespace. When two of them define the same key with different
 * text (e.g. `Total` in Orders/Details vs Orders/Receipt) nothing is dropped: the bare key keeps the
 * first view's text (alphabetical) and *every* variant is also emitted as `<View>.<key>`, in both
 * languages, so a screen can ask for the exact one. `collisions` lists them for the report.
 * Order of precedence for non-view sources: view resx → SharedResource → js/locales JSON.
 */
export function readSources(webRoot) {
  /** defs[lang][ns][key] = [{ from, view, value }] in precedence order */
  const defs = Object.fromEntries(LANGUAGES.map((lang) => [lang, {}]))
  const add = (lang, ns, key, value, from, view) => {
    const byKey = (defs[lang][ns] ??= {})
    ;(byKey[key] ??= []).push({ from, view, value: toI18nextPlaceholders(value) })
  }

  const resources = join(webRoot, 'Resources')
  const resxFiles = walk(resources)
    .filter((f) => f.endsWith('.resx'))
    .sort()
  for (const file of resxFiles) {
    const rel = relative(resources, file).split(sep).join('/')
    const lang = LANGUAGES.find((l) => file.endsWith(`.${l}.resx`))
    if (!lang) continue // neutral/other-language resx: not part of the SPA
    const parts = rel.split('/')
    // Views/<Folder>/<View>.<lang>.resx → namespace of <Folder>; SharedResource.<lang>.resx → common
    const isView = parts[0] === 'Views' && parts.length >= 3
    const ns = isView ? namespaceFromFolder(parts[1]) : SHARED_NAMESPACE
    const view = isView ? parts[parts.length - 1].replace(`.${lang}.resx`, '').replace(/^_+/, '') : null
    for (const [key, value] of parseResx(readFileSync(file, 'utf8'))) add(lang, ns, key, value, rel, view)
  }

  for (const lang of LANGUAGES) {
    const file = join(webRoot, 'wwwroot', 'js', 'locales', `${lang}.json`)
    const sections = JSON.parse(readFileSync(file, 'utf8'))
    for (const [section, content] of Object.entries(sections)) {
      const ns = section === 'common' ? SHARED_NAMESPACE : section
      const flat = content !== null && typeof content === 'object' ? flatten(content) : { [section]: String(content) }
      for (const [key, value] of Object.entries(flat)) add(lang, ns, key, value, `wwwroot/js/locales/${lang}.json`, null)
    }
  }

  const sources = Object.fromEntries(LANGUAGES.map((lang) => [lang, {}]))
  const collisions = []
  const namespaces = new Set(LANGUAGES.flatMap((lang) => Object.keys(defs[lang])))
  for (const ns of namespaces) {
    const keys = new Set(LANGUAGES.flatMap((lang) => Object.keys(defs[lang][ns] ?? {})))
    for (const key of keys) {
      const conflicted = LANGUAGES.some((lang) => new Set((defs[lang][ns]?.[key] ?? []).map((d) => d.value)).size > 1)
      if (conflicted) collisions.push({ ns, key, from: LANGUAGES.flatMap((lang) => (defs[lang][ns]?.[key] ?? []).map((d) => d.from)) })
      for (const lang of LANGUAGES) {
        const list = defs[lang][ns]?.[key] ?? []
        if (!list.length) continue
        const bucket = (sources[lang][ns] ??= {})
        bucket[key] = list[0].value
        if (conflicted) for (const d of list) if (d.view) bucket[`${d.view}.${key}`] = d.value
      }
    }
  }
  return { sources, collisions }
}

/** Old-app placeholders `{0}` / `{name}` → i18next's `{{0}}` / `{{name}}` (the only change made to a value). */
export function toI18nextPlaceholders(text) {
  return text.replace(/(?<!\{)\{([A-Za-z0-9_]+)\}(?!\})/g, '{{$1}}')
}

/** `{{0}}` / `{{name}}` tokens in a converted string. */
export function placeholders(text) {
  return [...text.matchAll(/\{\{([A-Za-z0-9_]+)\}\}/g)].map((m) => m[1]).sort()
}

/** Differences between the two languages of the *source* data. */
export function compareLanguages(sources) {
  const namespaces = new Set(LANGUAGES.flatMap((l) => Object.keys(sources[l])))
  const missing = []
  const placeholderMismatch = []
  let identical = 0
  for (const ns of [...namespaces].sort()) {
    const en = sources.en[ns] ?? {}
    const ar = sources.ar[ns] ?? {}
    for (const key of Object.keys(en)) if (!(key in ar)) missing.push({ ns, key, lang: 'ar' })
    for (const key of Object.keys(ar)) if (!(key in en)) missing.push({ ns, key, lang: 'en' })
    for (const key of Object.keys(en)) {
      if (!(key in ar)) continue
      if (en[key] === ar[key]) identical++
      if (placeholders(en[key]).join() !== placeholders(ar[key]).join()) placeholderMismatch.push({ ns, key })
    }
  }
  return { missing, placeholderMismatch, identical }
}

const byCodeUnit = (a, b) => (a < b ? -1 : a > b ? 1 : 0)

export function serialize(bucket) {
  const sorted = Object.fromEntries(Object.entries(bucket).sort(([a], [b]) => byCodeUnit(a, b)))
  return JSON.stringify(sorted, null, 2) + '\n'
}

/**
 * Merge generated keys into what is already on disk. Keys that only exist on disk (hand-added by a
 * track) are kept unless `prune`; keys from the sources always win, so edits to the .resx flow through.
 */
export function mergeBucket(existing, generated, { prune = false } = {}) {
  const base = prune ? {} : { ...existing }
  const added = Object.keys(generated).filter((k) => !(k in existing)).length
  const changed = Object.keys(generated).filter((k) => k in existing && existing[k] !== generated[k]).length
  const removed = prune ? Object.keys(existing).filter((k) => !(k in generated)).length : 0
  return { merged: Object.assign(base, generated), added, changed, removed }
}
