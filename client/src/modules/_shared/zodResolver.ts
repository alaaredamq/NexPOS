import type { FieldErrors, FieldValues, Resolver } from 'react-hook-form'
import type { ZodType } from 'zod'

/**
 * react-hook-form resolver for a zod schema (the `@hookform/resolvers` package is not a dependency: forms are small
 * and flat, so the 15 lines are cheaper). Only the first issue of each top-level field is kept, as a field shows one message.
 * Build the schema inside the component with the translated messages (`useMemo(() => z.object({...}), [t])`).
 */
export function zodResolver<T extends FieldValues>(schema: ZodType<T>): Resolver<T> {
  return async (values) => {
    const result = await schema.safeParseAsync(values)
    if (result.success) return { values: result.data, errors: {} }

    const errors: Record<string, { type: string; message: string }> = {}
    for (const issue of result.error.issues) {
      const field = String(issue.path[0] ?? '')
      if (field && !(field in errors)) errors[field] = { type: issue.code, message: issue.message }
    }
    return { values: {}, errors: errors as FieldErrors<T> }
  }
}
