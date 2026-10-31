// RTL guard (0C done-criteria): modules must use logical CSS properties, not physical ones.
// Scans client/src/**/*.css for properties that only make sense in LTR.
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../src', import.meta.url))
const bad = /(margin|padding)-(left|right)\s*:|(?<![\w-])(left|right)\s*:|text-align\s*:\s*(left|right)|float\s*:\s*(left|right)|border-(left|right)(-\w+)?\s*:/

const walk = (dir) =>
  readdirSync(dir, { withFileTypes: true }).flatMap((e) =>
    e.isDirectory() ? walk(join(dir, e.name)) : e.name.endsWith('.css') ? [join(dir, e.name)] : [],
  )

let hits = 0
for (const file of walk(root)) {
  readFileSync(file, 'utf8').split('\n').forEach((line, i) => {
    if (bad.test(line) && !line.includes('rtl-ok')) {
      console.error(`${file}:${i + 1}: physical direction -> ${line.trim()}`)
      hits++
    }
  })
}
if (hits) {
  console.error(`\n${hits} physical-direction CSS declaration(s). Use logical properties (margin-inline-start, inset-inline-start, text-align: start...). Append /* rtl-ok */ only for genuinely direction-independent cases.`)
  process.exit(1)
}
