import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { queryKeys } from '@/api/queryClient'

type Schemas = components['schemas']
export type StoreSettings = Schemas['StoreSettingsDto']
export type StoreSettingsInput = Schemas['StoreSettingsRequest']
export type PrintingSettings = Schemas['PrintingSettingsDto']
export type PrintingSettingsInput = Schemas['PrintingSettingsRequest']
export type PrinterTestResult = Schemas['PrinterTestResultDto']
export type BrandingSettings = Schemas['BrandingSettingsDto']

const root = ['settings'] as const
export const settingsKeys = {
  store: [...root, 'store'] as const,
  printing: [...root, 'printing'] as const,
  printers: [...root, 'printers'] as const,
  branding: [...root, 'branding'] as const,
  online: [...root, 'online'] as const,
}

export function useStoreSettings() {
  return useQuery({ queryKey: settingsKeys.store, queryFn: ({ signal }) => http.get<StoreSettings>('/settings/store', { signal }) })
}

export function useSaveStoreSettings() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: StoreSettingsInput) => http.put<StoreSettings>('/settings/store', input),
    onSuccess: (saved) => client.setQueryData(settingsKeys.store, saved),
  })
}

export function useResetStoreSettings() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => http.post<StoreSettings>('/settings/store/reset'),
    onSuccess: (saved) => client.setQueryData(settingsKeys.store, saved),
  })
}

export function usePrintingSettings() {
  return useQuery({ queryKey: settingsKeys.printing, queryFn: ({ signal }) => http.get<PrintingSettings>('/settings/printing', { signal }) })
}

export function useSavePrintingSettings() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: PrintingSettingsInput) => http.put<PrintingSettings>('/settings/printing', input),
    onSuccess: (saved) => client.setQueryData(settingsKeys.printing, saved),
  })
}

/** Printers installed on the machine that runs the server. Re-read on demand (`refetch`), not on every focus: listing them can take a moment. */
export function useInstalledPrinters() {
  return useQuery({
    queryKey: settingsKeys.printers,
    queryFn: ({ signal }) => http.get<{ items: string[] }>('/settings/printers', { signal }).then((r) => r.items),
    refetchOnWindowFocus: false,
    staleTime: 60_000,
  })
}

export function useTestPrinter() {
  return useMutation({ mutationFn: (printerName: string) => http.post<PrinterTestResult>('/settings/printers/test', { printerName }) })
}

export function useBrandingSettings() {
  return useQuery({ queryKey: settingsKeys.branding, queryFn: ({ signal }) => http.get<BrandingSettings>('/settings/branding', { signal }) })
}

export interface BrandingInput {
  appName: string
  logo: File | null
  favicon: File | null
  removeLogo: boolean
  removeFavicon: boolean
}

/** The shell reads the app name, logo and favicon from the session, so a save refreshes it too. */
export function useSaveBranding() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: BrandingInput) => {
      const form = new FormData()
      form.set('appName', input.appName)
      if (input.logo) form.set('logo', input.logo)
      if (input.favicon) form.set('favicon', input.favicon)
      form.set('removeLogo', String(input.removeLogo))
      form.set('removeFavicon', String(input.removeFavicon))
      return http.put<BrandingSettings>('/settings/branding', form)
    },
    onSuccess: (saved) => {
      client.setQueryData(settingsKeys.branding, saved)
      return client.invalidateQueries({ queryKey: queryKeys.session })
    },
  })
}

// ---- Online (cloud mirror) ----

export type OnlineStatus = Schemas['OnlineStatusDto']

export function useOnlineStatus() {
  return useQuery({
    queryKey: settingsKeys.online,
    queryFn: ({ signal }) => http.get<OnlineStatus>('/online', { signal }),
    // The background sync reports its progress here; checking every few seconds keeps "last synced" honest without a manual refresh.
    refetchInterval: 10_000,
  })
}

export function useConnectOnline() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (link: string) => http.post<OnlineStatus>('/online/connect', { link }),
    onSuccess: (status) => client.setQueryData(settingsKeys.online, status),
  })
}

export function useDisconnectOnline() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => http.post<OnlineStatus>('/online/disconnect'),
    onSuccess: (status) => client.setQueryData(settingsKeys.online, status),
  })
}

export function useSyncOnlineNow() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => http.post<OnlineStatus>('/online/sync-now'),
    onSuccess: (status) => client.setQueryData(settingsKeys.online, status),
    // The push happens in the background; look again shortly for its result.
    onSettled: () => window.setTimeout(() => void client.invalidateQueries({ queryKey: settingsKeys.online }), 2500),
  })
}

// ---- Private Online: the recovery slip and the key (docs/online/PRIVACY-KEYS.md) ----
// Everything that reveals the key sends the signed-in user's PIN in the request; the server checks it every time.

export type RecoverySlip = Schemas['RecoverySlipDto']
export type SlipPrintResult = Schemas['SlipPrintResultDto']

/** The recovery code for the key on this PC (the key is made on first use). A wrong PIN is a 400 with `errors.pin`. */
export function getRecoverySlip(pin: string): Promise<RecoverySlip> {
  return http.post<RecoverySlip>('/online/sealing/slip', { pin })
}

/** Prints the slip on the receipt printer. The API never fails because of the printer: `printed: false` comes with a message. */
export function printRecoverySlip(pin: string): Promise<SlipPrintResult> {
  return http.post<SlipPrintResult>('/online/sealing/slip/print', { pin })
}

/** Saves the slip as a PDF through the signed-in cookie. */
export async function downloadRecoverySlipPdf(pin: string): Promise<void> {
  const file = await http.downloadPost('/online/sealing/slip/pdf', { pin })
  const url = URL.createObjectURL(file.blob)
  const link = document.createElement('a')
  link.href = url
  link.download = file.filename || 'recovery-slip.pdf'
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

/** The owner kept the slip: sealing starts and the whole shop is uploaded again, locked. */
export function useConfirmSealing() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: () => http.post<OnlineStatus>('/online/sealing/confirm'),
    onSuccess: (status) => client.setQueryData(settingsKeys.online, status),
  })
}

/** Puts the key from a recovery slip on this PC (a restore on a new machine). */
export function useRestoreSealKey() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: { pin: string; code: string }) => http.post<OnlineStatus>('/online/sealing/restore', input),
    onSuccess: (status) => client.setQueryData(settingsKeys.online, status),
  })
}
