import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { queryKeys } from '@/api/queryClient'

export type LicenseDetails = components['schemas']['LicenseDto']
export type TillSeats = components['schemas']['TillSeatsDto']
export type TillDevice = components['schemas']['TillDeviceDto']
export type PairingCode = components['schemas']['PairingCodeDto']
export type DeviceStatus = components['schemas']['DeviceStatusDto']

export const licenseKeys = { all: ['license'] as const, tills: ['license', 'tills'] as const, device: ['license', 'device'] as const }

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

// ---- the PCs of a multi-seat package -----------------------------------------------------------------------------------------------

/** The package and its tills (administrators). Refreshed while open so a pairing code turning into an active till shows up on its own. */
export function useTillSeats(enabled: boolean) {
  return useQuery({
    queryKey: licenseKeys.tills,
    queryFn: ({ signal }) => http.get<TillSeats>('/devices', { signal }),
    enabled,
    refetchInterval: 10_000,
  })
}

/** Reserves a seat and returns the one-time code to type on the till (409 `seats_full` when the package is used up). */
export function useCreatePairingCode() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (name: string) => http.post<PairingCode>('/devices/pairing-codes', { name }),
    onSuccess: () => client.invalidateQueries({ queryKey: licenseKeys.tills }),
  })
}

/** Removes a till, or cancels an unused code, and frees the seat. */
export function useRemoveTill() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (id: number) => http.delete<void>(`/devices/${id}`),
    onSuccess: () => client.invalidateQueries({ queryKey: licenseKeys.tills }),
  })
}

/** What this PC is, for the public pairing screen: the store server, an allowed till, or not allowed to sell. */
export function useDeviceStatus() {
  return useQuery({ queryKey: licenseKeys.device, queryFn: ({ signal }) => http.get<DeviceStatus>('/devices/me', { signal }) })
}

/** Called by the till itself: trades the code for the device cookie. 400 `invalid_pairing_code`, 429 after too many wrong ones. */
export function usePairTill() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (code: string) => http.post<{ name: string }>('/devices/pair', { code }),
    onSuccess: () => client.invalidateQueries({ queryKey: licenseKeys.device }),
  })
}
