// `npm run i18n:lint` — every literal translation key used in client/src must exist in locales/en (D8 / 0E).
// Also warns (exit 0) about keys present in en but missing in ar for the namespaces the code uses,
// and fails on those too with --strict-ar.

import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { extractKeys, findMissing } from './lib/keys.mjs'

const clientRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const src = join(clientRoot, 'src')
const strictAr = process.argv.includes('--strict-ar')

function walk(dir) {
  return readdirSync(dir).flatMap((name) => {
    const full = join(dir, name)
    if (statSync(full).isDirectory()) return name === 'locales' ? [] : walk(full)
    // tests use made-up keys on purpose
    return /\.(ts|tsx)$/.test(name) && !/\.(d|test)\.tsx?$/.test(name) ? [full] : []
  })
}

function loadBundle(lang) {
  const dir = join(src, 'locales', lang)
  const bundle = {}
  if (!existsSync(dir)) return bundle
  for (const name of readdirSync(dir)) {
    if (name.endsWith('.json')) bundle[name.slice(0, -5)] = JSON.parse(readFileSync(join(dir, name), 'utf8'))
  }
  return bundle
}

const en = loadBundle('en')
const ar = loadBundle('ar')
let used = 0
const missingEn = []
const missingAr = []

for (const file of walk(src)) {
  const keys = extractKeys(readFileSync(file, 'utf8'))
  used += keys.length
  const where = relative(clientRoot, file).split('\\').join('/')
  for (const k of findMissing(keys, en)) missingEn.push(`${where}:${k.line}  "${k.key}" not in en/{${k.ns.join(',')}}`)
  for (const k of findMissing(keys, ar)) {
    if (!missingEn.some((m) => m.startsWith(`${where}:${k.line} `))) missingAr.push(`${where}:${k.line}  "${k.key}" not in ar/{${k.ns.join(',')}}`)
  }
}

if (missingAr.length) {
  console.warn(`i18n:lint  ${missingAr.length} key(s) have no Arabic text (en is the fallback):`)
  for (const line of missingAr) console.warn(`  ${line}`)
}
if (missingEn.length) {
  console.error(`i18n:lint  ${missingEn.length} key(s) missing from en — add them to the .resx and run \`npm run i18n:export\`, or to locales/en and locales/ar:`)
  for (const line of missingEn) console.error(`  ${line}`)
  process.exit(1)
}
if (strictAr && missingAr.length) process.exit(1)
console.log(`i18n:lint  ${used} literal key(s) checked, none missing from en`)
