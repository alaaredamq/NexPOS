// Static extraction of translation keys from TS/TSX source for `npm run i18n:lint`.
// Best effort by design: only string-literal keys are checked; `t(variable)` and template literals with
// `${}` are skipped. Namespace comes from the hook in the same file (`useTranslation('orders')`) or from an
// explicit `{ ns: 'common' }` option on the call.

const DEFAULT_NAMESPACE = 'common'

const STRING = String.raw`(?:'((?:\\.|[^'\\])*)'|"((?:\\.|[^"\\])*)"|\x60((?:\\.|[^\x60\\$]|\$(?!\{))*)\x60)`
const unescape = (s) => s.replace(/\\(.)/g, '$1')
const pick = (m, from) => unescape(m[from] ?? m[from + 1] ?? m[from + 2] ?? '')

/** Namespaces a file opts into: `useTranslation('a')`, `useTranslation(['a','b'])`, `loadNamespaces('a','b')`. */
export function declaredNamespaces(source) {
  const found = []
  for (const m of source.matchAll(/\b(?:useTranslation|loadNamespaces|getFixedT)\s*\(([^)]*)\)/g)) {
    for (const s of (m[1] ?? '').matchAll(new RegExp(STRING, 'g'))) found.push(pick(s, 1))
  }
  return found
}

/** Text of the call's arguments after the key, up to the matching `)` (so `{ ns: 'x' }` can be found). */
function argumentTail(source, from) {
  let depth = 1
  for (let i = from; i < source.length; i++) {
    const c = source[i]
    if (c === '(') depth++
    else if (c === ')' && --depth === 0) return source.slice(from, i)
  }
  return source.slice(from)
}

/** Blank out comments (keeping newlines, so line numbers stay right). Strings containing `//` are left alone. */
export function stripComments(source) {
  return source.replace(
    new RegExp(String.raw`${STRING}|(\/\/[^\n]*|\/\*[\s\S]*?\*\/)`, 'g'),
    (whole, s1, s2, s3, comment) => (comment === undefined ? whole : comment.replace(/[^\n]/g, ' ')),
  )
}

/** → [{ key, ns, line }] for every literal `t('Key')` / `i18n.t('Key')` / `<Trans i18nKey="Key" />`. */
export function extractKeys(rawSource) {
  const source = stripComments(rawSource)
  const fileNs = declaredNamespaces(source)
  const keys = []
  const lineOf = (index) => source.slice(0, index).split('\n').length
  const nsFor = (tail) => {
    const explicit = new RegExp(String.raw`\bns\s*:\s*` + STRING).exec(tail)
    if (explicit) return [pick(explicit, 1)]
    return fileNs.length ? fileNs : [DEFAULT_NAMESPACE]
  }

  for (const m of source.matchAll(new RegExp(String.raw`(?<![\w$])t\s*\(\s*` + STRING, 'g'))) {
    const end = m.index + m[0].length
    keys.push({ key: pick(m, 1), ns: nsFor(argumentTail(source, end)), line: lineOf(m.index) })
  }
  for (const m of source.matchAll(new RegExp(String.raw`\bi18nKey\s*=\s*\{?\s*` + STRING, 'g'))) {
    keys.push({ key: pick(m, 1), ns: fileNs.length ? fileNs : [DEFAULT_NAMESPACE], line: lineOf(m.index) })
  }
  return keys
}

/** A key is fine when ANY candidate namespace has it (a file may open several namespaces). */
export function findMissing(keys, bundle) {
  return keys.filter(({ key, ns }) => !ns.some((n) => bundle[n] && key in bundle[n]))
}
