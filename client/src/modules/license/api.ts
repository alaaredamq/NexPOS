import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { queryKeys } from '@/api/queryClient'

export type LicenseDetails = components['schemas']['LicenseDto']

export const licenseKeys = { all: ['license'] as const }

export function useLicenseDetails() {
  return useQuery({ queryKey: licenseKeys.all, queryFn: ({ signal }) => http.get<LicenseDetails>('/license', { signal }) })
}

/**
 * Installs a pasted key. A refused key is a 400 `validation_failed` with the reason on `licenseKey`. On success the shell's banner and
 * the sales routes must follow at once, so the session (which carries the short licence summary) is refreshed with this page's copy.
 */
export function useActivateLicense() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (licenseKey: string) => http.post<LicenseDetails>('/license/activate', { licenseKey }),
    onSuccess: async (license) => {
      client.setQueryData(licenseKeys.all, license)
      await client.invalidateQueries({ queryKey: queryKeys.session })
    },
  })
}
