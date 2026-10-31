// Fails the build when the entry bundle goes over the D12 budget (REACT_MIGRATION.md).
// Shell + login <= 250 KB gzip. Per-route budgets (POS <= 350 KB) are added by the P tracks as
// they land; add them to ROUTE_BUDGETS keyed by the lazy chunk's name prefix.
import { readdirSync, readFileSync } from 'node:fs'
import { gzipSync } from 'node:zlib'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const dist = fileURLToPath(new URL('../../Web/wwwroot/app', import.meta.url))
const KB = 1024
const ENTRY_BUDGET = 250 * KB
const ROUTE_BUDGETS = {} // e.g. { pos: 350 * KB }

const html = readFileSync(join(dist, 'index.html'), 'utf8')
const gz = (file) => gzipSync(readFileSync(join(dist, file))).length
const refs = [...html.matchAll(/(?:src|href)="\/app\/(assets\/[^"]+\.(?:js|css))"/g)].map((m) => m[1])

let failed = false
const entry = refs.reduce((n, f) => n + gz(f), 0)
const report = (name, size, budget) => {
  const over = size > budget
  failed ||= over
  console.log(`${over ? 'FAIL' : 'ok  '} ${name}: ${(size / KB).toFixed(1)} KB gzip (budget ${budget / KB} KB)`)
}
report('entry (shell + login)', entry, ENTRY_BUDGET)

const assets = readdirSync(join(dist, 'assets')).filter((f) => f.endsWith('.js') && !refs.includes(`assets/${f}`))
for (const [prefix, budget] of Object.entries(ROUTE_BUDGETS)) {
  const files = assets.filter((f) => f.startsWith(`${prefix}-`))
  if (files.length) report(`route ${prefix}`, files.reduce((n, f) => n + gz(`assets/${f}`), 0), budget)
}
process.exit(failed ? 1 : 0)
