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
