import type { FieldValues, Path, UseFormReturn } from 'react-hook-form'

/** RFC 7807 validation payload from the API (REACT_MIGRATION §6 0A): `errors{ field: [messages] }`. */
export interface ProblemLike {
  title?: string
  detail?: string
  code?: string
  errors?: Record<string, string[]>
}

const camel = (s: string) => (s ? s[0]!.toLowerCase() + s.slice(1) : s)

/**
 * Pushes the API's `errors{}` onto the matching react-hook-form fields (server keys are PascalCase or
 * camelCase, possibly dotted like `Lines[0].Qty`). Returns the messages that matched no field so the
 * caller can show them in a <FormError> / toast instead of losing them.
 *
 * Field matching uses the form's current values, so give every field a default value (`''`, `null`, …).
 */
export function applyServerErrors<T extends FieldValues>(
  form: Pick<UseFormReturn<T>, 'setError' | 'getValues'>,
  problem: ProblemLike | undefined,
): string[] {
  const unmatched: string[] = []
  const known = new Set(Object.keys(flatten(form.getValues())))
  const errors = problem?.errors ?? {}
  let first = true

  for (const [rawKey, messages] of Object.entries(errors)) {
    const message = messages[0]
    if (!message) continue
    // `Lines[0].Qty` → `lines.0.qty` (react-hook-form's path syntax accepts both, flatten() emits dots).
    const path = rawKey
      .replace(/\[(\d+)\]/g, '.$1')
      .split('.')
      .map(camel)
      .join('.')
    if (rawKey && known.has(path)) {
      form.setError(path as Path<T>, { type: 'server', message }, { shouldFocus: first })
      first = false
    } else {
      unmatched.push(message)
    }
  }
  if (Object.keys(errors).length === 0 && problem?.detail) unmatched.push(problem.detail)
  return unmatched
}

function flatten(obj: unknown, prefix = '', out: Record<string, true> = {}): Record<string, true> {
  if (obj && typeof obj === 'object') {
    for (const [k, v] of Object.entries(obj)) {
      const p = prefix ? `${prefix}.${k}` : k
      out[p] = true
      flatten(v, p, out)
    }
  }
  return out
}
