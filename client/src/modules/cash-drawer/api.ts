import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { useRefreshSession } from '@/core/auth/useSession'

type Schemas = components['schemas']
export type CurrentDrawer = Schemas['CurrentDrawerDto']
export type DrawerSession = Schemas['DrawerSessionDto']
export type DrawerHistoryRow = Schemas['DrawerHistoryRowDto']
export type DrawerHistoryPage = Schemas['DrawerHistoryRowDtoPagedResult']
export type CashDrop = Schemas['CashDropDto']
export type ZReport = Schemas['ZReportDto']
export type ZReportOrder = Schemas['ZReportOrderDto']
export type ZReportPrintResult = Schemas['ZReportPrintResultDto']
export type OpenDrawerInput = Schemas['OpenDrawerRequest']
export type CloseDrawerInput = Schemas['CloseDrawerRequest']
export type CashDropInput = Schemas['CashDropRequest']

/** The drawer is dollar-denominated, as the Razor pages, the receipt printer and the shell chip all print it. */
export const DRAWER_CURRENCY = '$'

/** The running figures of an open session are refetched this often (the Razor page polled every 30 s). */
export const RUNNING_REFRESH_MS = 30_000

export interface HistoryParams {
  page: number
  pageSize: number
  /** `yyyy-mm-dd` local days, both included; `''` is open-ended. */
  from: string
  to: string
}

const root = ['cash-drawer'] as const
export const drawerKeys = {
  all: root,
  current: [...root, 'current'] as const,
  history: (params: HistoryParams) => [...root, 'history', params] as const,
  session: (id: number) => [...root, 'session', id] as const,
  zReport: (id: number) => [...root, 'z-report', id] as const,
}

export function useCurrentDrawer() {
  return useQuery({
    queryKey: drawerKeys.current,
    queryFn: ({ signal }) => http.get<CurrentDrawer>('/cash-drawer/current', { signal }),
    refetchInterval: RUNNING_REFRESH_MS,
  })
}

export function useDrawerHistory(params: HistoryParams) {
  return useQuery({
    queryKey: drawerKeys.history(params),
    queryFn: ({ signal }) => http.get<DrawerHistoryPage>('/cash-drawer/sessions', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

/** One session; an open one keeps its running figures fresh. */
export function useDrawerSession(id: number | null) {
  return useQuery({
    queryKey: drawerKeys.session(id ?? 0),
    queryFn: ({ signal }) => http.get<DrawerSession>(`/cash-drawer/sessions/${id}`, { signal }),
    enabled: id !== null,
    refetchInterval: (query) => (query.state.data?.isOpen ? RUNNING_REFRESH_MS : false),
  })
}

export function useZReport(id: number) {
  return useQuery({
    queryKey: drawerKeys.zReport(id),
    queryFn: ({ signal }) => http.get<ZReport>(`/cash-drawer/sessions/${id}/z-report`, { signal }),
  })
}

/**
 * Every change to the drawer refetches the drawer queries *and* the session payload: the shell chip and the till read
 * `session.drawer`, so they must not wait for the 60 s refresh to notice that the drawer was opened or closed.
 */
function useDrawerMutation<TInput, TResult>(send: (input: TInput) => Promise<TResult>) {
  const client = useQueryClient()
  const refreshSession = useRefreshSession()
  return useMutation({
    mutationFn: send,
    onSuccess: async () => {
      await Promise.all([client.invalidateQueries({ queryKey: drawerKeys.all }), refreshSession()])
    },
  })
}

export function useOpenDrawer() {
  return useDrawerMutation((input: OpenDrawerInput) => http.post<DrawerSession>('/cash-drawer/open', input))
}

export function useCloseDrawer() {
  return useDrawerMutation(({ id, ...input }: CloseDrawerInput & { id: number }) =>
    http.post<DrawerSession>(`/cash-drawer/sessions/${id}/close`, input),
  )
}

export function useAddCashDrop() {
  return useDrawerMutation(({ id, ...input }: CashDropInput & { id: number }) => http.post<CashDrop>(`/cash-drawer/sessions/${id}/drops`, input))
}

/** Sends the Z report to the receipt printer. The API never fails because of the printer: `printed: false` comes with a message. */
export function printZReport(id: number): Promise<ZReportPrintResult> {
  return http.post<ZReportPrintResult>(`/cash-drawer/sessions/${id}/z-report/print`)
}

/** Saves the Z report PDF through the signed-in cookie. */
export async function downloadZReportPdf(id: number): Promise<void> {
  const file = await http.download(`/cash-drawer/sessions/${id}/z-report.pdf`)
  const url = URL.createObjectURL(file.blob)
  const link = document.createElement('a')
  link.href = url
  link.download = file.filename || `z-report-${id}.pdf`
  document.body.append(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}
