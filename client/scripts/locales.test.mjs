// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { flatten, mergeBucket, toI18nextPlaceholders, namespaceFromFolder, parseResx, placeholders, readSources, serialize } from './lib/locales.mjs'
import { declaredNamespaces, extractKeys, findMissing, stripComments } from './lib/keys.mjs'

const clientRoot = resolve(fileURLToPath(new URL('.', import.meta.url)), '..')

describe('parseResx', () => {
  const resx = `<root>
    <xsd:element name="data"><xsd:complexType/></xsd:element>
    <data name="A.b: c..." xml:space="preserve"><value>Tom &amp; Jerry &lt;3 &quot;x&quot; &#x627;</value></data>
    <data name="Empty" xml:space="preserve"><value></value></data>
    <data name="Cdata"><value><![CDATA[a < b]]></value></data>
    <data name="Icon" type="System.Resources.ResXFileRef" mimetype="application/x"><value>x.png</value></data>
  </root>`

  it('reads names and values verbatim, decoding XML entities', () => {
    const map = parseResx(resx)
    expect(map.get('A.b: c...')).toBe('Tom & Jerry <3 "x" ا')
    expect(map.get('Empty')).toBe('')
    expect(map.get('Cdata')).toBe('a < b')
  })

  it('skips binary/typed resources and the XSD schema block', () => {
    expect([...parseResx(resx).keys()]).toEqual(['A.b: c...', 'Empty', 'Cdata'])
  })
})

describe('namespaces and flattening', () => {
  it('maps view folders to lower-camel namespaces', () => {
    expect(namespaceFromFolder('POS')).toBe('pos')
    expect(namespaceFromFolder('CashDrawer')).toBe('cashDrawer')
    expect(namespaceFromFolder('ProductDisplayOrder')).toBe('productDisplayOrder')
    expect(namespaceFromFolder('AuditLogs')).toBe('auditLogs')
    expect(namespaceFromFolder('Shared')).toBe('layout')
    expect(namespaceFromFolder('DBBackup')).toBe('dbBackup')
  })

  it('flattens nested JSON sections with dotted paths', () => {
    expect(flatten({ a: { b: 'x', c: { d: 'y' } }, e: 'z' })).toEqual({ 'a.b': 'x', 'a.c.d': 'y', e: 'z' })
  })

  it('extracts placeholders', () => {
    expect(placeholders('Pack of {{0}} / {{name}} {{0}}')).toEqual(['0', '0', 'name'])
    expect(toI18nextPlaceholders('Pack of {0} / {name} {{kept}} ${0}')).toBe('Pack of {{0}} / {{name}} {{kept}} ${{0}}')
    expect(placeholders('plain')).toEqual([])
  })
})

describe('mergeBucket', () => {
  it('keeps hand-added keys, lets the source win, and counts changes', () => {
    const r = mergeBucket({ keep: 'mine', edit: 'old' }, { edit: 'new', add: 'x' })
    expect(r.merged).toEqual({ keep: 'mine', edit: 'new', add: 'x' })
    expect([r.added, r.changed, r.removed]).toEqual([1, 1, 0])
  })
  it('drops unknown keys with prune', () => {
    const r = mergeBucket({ keep: 'mine', edit: 'old' }, { edit: 'new' }, { prune: true })
    expect(r.merged).toEqual({ edit: 'new' })
    expect(r.removed).toBe(1)
  })
  it('serializes sorted, 2-space, trailing newline', () => {
    expect(serialize({ b: '1', a: '2' })).toBe('{\n  "a": "2",\n  "b": "1"\n}\n')
  })
})

describe('readSources against the real Web/Resources', () => {
  const { sources } = readSources(resolve(clientRoot, '..', 'Web'))

  it('produces both languages with the expected namespaces', () => {
    expect(Object.keys(sources.en)).toEqual(expect.arrayContaining(['common', 'pos', 'orders', 'cashDrawer', 'layout']))
    expect(sources.en.pos?.['pos.cartIsEmpty'] ?? sources.en.pos?.['cartIsEmpty']).toBeTruthy()
  })

  it('keeps colliding view keys addressable per view', () => {
    expect(sources.en.orders?.['Receipt.Total']).toBeDefined()
    expect(sources.ar.orders?.['Receipt.Total']).toBeDefined()
  })
})

describe('committed locale files are in sync with the sources', () => {
  it('every source key is present in client/src/locales (run `npm run i18n:export` if this fails)', () => {
    const { sources } = readSources(resolve(clientRoot, '..', 'Web'))
    const stale = []
    for (const lang of ['en', 'ar']) {
      for (const [ns, bucket] of Object.entries(sources[lang])) {
        const file = join(clientRoot, 'src', 'locales', lang, `${ns}.json`)
        const disk = existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : {}
        for (const [key, value] of Object.entries(bucket)) if (disk[key] !== value) stale.push(`${lang}/${ns}: ${key}`)
      }
    }
    expect(stale.slice(0, 5)).toEqual([])
    expect(readdirSync(join(clientRoot, 'src', 'locales', 'en')).length).toBeGreaterThan(10)
  })
})

describe('key extraction (i18n:lint)', () => {
  const code = `
    const { t } = useTranslation('orders')
    t('OrdersManagement')
    t("Signing in...", { x: 1 })
    t('Total', { ns: 'common' })
    t(\`Template\`)
    t(\`dyn\${x}\`)
    t(variable)
    foo.set('x')
    <Trans i18nKey="Refresh" />
  `
  it('finds declared namespaces', () => {
    expect(declaredNamespaces(code)).toEqual(['orders'])
    expect(declaredNamespaces("useTranslation(['a', \"b\"])")).toEqual(['a', 'b'])
  })

  it('extracts literal keys with their namespace, skipping dynamic ones', () => {
    const found = extractKeys(code).map((k) => [k.key, k.ns.join()])
    expect(found).toEqual([
      ['OrdersManagement', 'orders'],
      ['Signing in...', 'orders'],
      ['Total', 'common'],
      ['Template', 'orders'],
      ['Refresh', 'orders'],
    ])
  })

  it('reports keys missing from the bundle', () => {
    const keys = extractKeys(code)
    const missing = findMissing(keys, { orders: { 'OrdersManagement': 'x', Refresh: 'y' }, common: { Total: 'z' } })
    expect(missing.map((k) => k.key)).toEqual(['Signing in...', 'Template'])
  })

  it('ignores keys inside comments but not strings containing //', () => {
    const src = ["// t('Commented')", "/* t('Block') */", "t('http://x')"].join('\n')
    expect(stripComments(src).split('\n')).toHaveLength(3)
    expect(extractKeys(src).map((k) => k.key)).toEqual(['http://x'])
  })

  it('defaults to common when the file declares no namespace', () => {
    expect(extractKeys("t('Save')")[0]?.ns).toEqual(['common'])
  })
})
