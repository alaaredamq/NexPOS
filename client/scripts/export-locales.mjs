// Re-runnable converter: old-app translations → client/src/locales/{en,ar}/<namespace>.json   (D8, section 0E)
//
//   npm run i18n:export                 merge sources into the JSON files (keeps keys a track hand-added)
//   npm run i18n:export -- --check      write nothing; exit 1 if the files are out of date (CI / pre-merge)
//   npm run i18n:export -- --prune      also delete keys that no source defines
//   npm run i18n:export -- --strict     exit 1 if any key exists in only one language
//   npm run i18n:export -- --verbose    list every missing key / collision instead of the first few
//
// Sources: Web/Resources/SharedResource.{en,ar}.resx → `common`; Web/Resources/Views/<Folder>/*.{en,ar}.resx → <folder>;
//          Web/wwwroot/js/locales/{en,ar}.json → one namespace per top-level section (flattened `a.b` keys).
// Keys and values are copied verbatim; `{0}` / `{name}` placeholders are kept as they are.

import { existsSync, mkdirSync, readdirSync, readFileSync, unlinkSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { LANGUAGES, compareLanguages, mergeBucket, readSources, serialize } from './lib/locales.mjs'

const clientRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const webRoot = resolve(clientRoot, '..', 'Web')
const localesRoot = join(clientRoot, 'src', 'locales')

const flags = new Set(process.argv.slice(2))
const check = flags.has('--check')
const prune = flags.has('--prune')
const strict = flags.has('--strict')
const verbose = flags.has('--verbose')

const { sources, collisions } = readSources(webRoot)
const comparison = compareLanguages(sources)

let dirty = 0
const rows = []
for (const lang of LANGUAGES) {
  for (const [ns, generated] of Object.entries(sources[lang]).sort(([a], [b]) => (a < b ? -1 : 1))) {
    const file = join(localesRoot, lang, `${ns}.json`)
    const existing = existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : {}
    const { merged, added, changed, removed } = mergeBucket(existing, generated, { prune })
    const text = serialize(merged)
    // git may check files out with CRLF (autocrlf on Windows); content, not line endings, decides "out of date"
    const current = existsSync(file) ? readFileSync(file, 'utf8').replaceAll('\r\n', '\n') : null
    if (text !== current) {
      dirty++
      if (!check) {
        mkdirSync(dirname(file), { recursive: true })
        writeFileSync(file, text)
      }
    }
    rows.push({ lang, ns, keys: Object.keys(merged).length, added, changed, removed })
  }
}

// --prune also retires namespaces that no source defines any more (e.g. a renamed view folder).
const orphans = []
if (prune) {
  for (const lang of LANGUAGES) {
    const dir = join(localesRoot, lang)
    if (!existsSync(dir)) continue
    for (const name of readdirSync(dir)) {
      if (name.endsWith('.json') && !(name.slice(0, -5) in sources[lang])) orphans.push(join(dir, name))
    }
  }
  dirty += orphans.length
  if (!check) for (const file of orphans) unlinkSync(file)
}

// ── report ────────────────────────────────────────────────────────────────────────────────────────
const total = (lang) => rows.filter((r) => r.lang === lang).reduce((n, r) => n + r.keys, 0)
const namespaces = [...new Set(rows.map((r) => r.ns))]
console.log(`i18n:export  ${namespaces.length} namespaces · en ${total('en')} keys · ar ${total('ar')} keys`)
for (const ns of namespaces) {
  const en = rows.find((r) => r.lang === 'en' && r.ns === ns)
  const ar = rows.find((r) => r.lang === 'ar' && r.ns === ns)
  console.log(`  ${ns.padEnd(22)} en ${String(en?.keys ?? 0).padStart(4)}   ar ${String(ar?.keys ?? 0).padStart(4)}`)
}

const show = (list, format) => {
  const limit = verbose ? list.length : 8
  for (const item of list.slice(0, limit)) console.log(`    ${format(item)}`)
  if (list.length > limit) console.log(`    … ${list.length - limit} more (use --verbose)`)
}

if (comparison.missing.length) {
  console.log(`\n  ${comparison.missing.length} key(s) exist in only one language (not copied to the other; en is the fallback):`)
  show(comparison.missing, (m) => `${m.ns}: "${m.key}" has no ${m.lang} text`)
}
if (comparison.placeholderMismatch.length) {
  console.log(`\n  ${comparison.placeholderMismatch.length} key(s) whose {placeholders} differ between en and ar:`)
  show(comparison.placeholderMismatch, (m) => `${m.ns}: "${m.key}"`)
}
if (collisions.length) {
  console.log(`
  ${collisions.length} key(s) defined with different text by several views of one folder — bare key = first view, every variant also as "<View>.<key>":`)
  show(collisions, (c) => `${c.ns}: "${c.key}"  (${[...new Set(c.from)].join(', ')})`)
}
if (orphans.length) {
  console.log(`\n  ${orphans.length} orphaned namespace file(s) ${check ? 'to remove' : 'removed'}: ${orphans.map((f) => f.slice(localesRoot.length + 1)).join(', ')}`)
}
console.log(`\n  ${comparison.identical} key(s) have identical en/ar text (often fine: "PIN", "OK"; otherwise untranslated).`)

if (check) {
  console.log(dirty ? `\n✗ ${dirty} locale file(s) out of date — run: npm run i18n:export` : '\n✓ locale files up to date')
  process.exit(dirty ? 1 : 0)
}
console.log(`\n✓ ${dirty} file(s) written`)
if (strict && comparison.missing.length) process.exit(1)
