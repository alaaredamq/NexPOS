import { useState } from 'react'
import type { FieldValues, UseFormReturn } from 'react-hook-form'
import { isApiError } from '@/api/http'
import { applyServerErrors } from '@/ui/form/serverErrors'

/**
 * What every dialog of the module does around its save: run it, and when the API refuses, put the message on the field it names
 * (400 `validation_failed`) or keep it for the form-level banner (409 rules such as "the default currency cannot be deleted").
 * `onMissing` is for a 404: somebody else deleted the row while the dialog was open.
 */
export function useFormRunner<V extends FieldValues>(form: UseFormReturn<V>, fallback: string, onMissing?: () => void) {
  const [formErrors, setFormErrors] = useState<string[]>([])

  const run = async (action: () => Promise<void>) => {
    setFormErrors([])
    try {
      await action()
    } catch (error) {
      if (isApiError(error) && error.status === 404 && onMissing) {
        onMissing()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [fallback])
    }
  }

  return { formErrors, run }
}
