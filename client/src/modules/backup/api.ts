import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { queryKeys } from '@/api/queryClient'

type Schemas = components['schemas']
export type BackupRow = Schemas['BackupDto']
export type BackupList = Schemas['BackupDtoPagedResult']
export type BackupSummary = Schemas['BackupSummaryDto']
export type BackupValidation = Schemas['BackupValidationDto']
export type BulkValidateResult = Schemas['BackupBulkValidateResult']
export type BackupConnection = Schemas['BackupConnectionDto']
export type ScheduleRow = Schemas['BackupScheduleDto']
export type ScheduleInput = Schemas['BackupScheduleRequest']

/** The statuses a backup can be filtered by in the list (a failed one stays listed so the reason is not lost). */
export const STATUS_FILTERS = ['', 'Completed', 'Failed'] as const

export interface BackupListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
  status: string
}

const root = ['backup'] as const
export const backupKeys = {
  all: root,
  summary: [...root, 'summary'] as const,
  list: (params: BackupListParams) => [...root, 'list', params] as const,
  schedules: [...root, 'schedules'] as const,
}

export function useBackupSummary() {
  return useQuery({ queryKey: backupKeys.summary, queryFn: ({ signal }) => http.get<BackupSummary>('/backups/summary', { signal }) })
}

export function useBackups(params: BackupListParams, enabled = true) {
  return useQuery({
    queryKey: backupKeys.list(params),
    queryFn: ({ signal }) => http.get<BackupList>('/backups', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
    enabled,
  })
}

export function useSchedules() {
  return useQuery({ queryKey: backupKeys.schedules, queryFn: ({ signal }) => http.get<ScheduleRow[]>('/backup-schedules', { signal }) })
}

/** Every backup action changes the list, the tiles and (a schedule run) the schedule's last run: refresh them together. */
function useInvalidate() {
  const client = useQueryClient()
  return () => client.invalidateQueries({ queryKey: backupKeys.all })
}

export function useCreateBackup() {
  const invalidate = useInvalidate()
  // A snapshot that fails is recorded too, so the list must refresh on failure as well.
  return useMutation({
    mutationFn: (input: { name?: string; description?: string }) => http.post<BackupRow>('/backups', input),
    onSettled: invalidate,
  })
}

export function useValidateBackup() {
  return useMutation({ mutationFn: (id: number) => http.post<BackupValidation>(`/backups/${id}/validate`) })
}

export function useDeleteBackup() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.delete<void>(`/backups/${id}`), onSuccess: invalidate })
}

export function useBulkDelete() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: (ids: number[]) => http.post<{ deleted: number }>('/backups/bulk-delete', { ids }),
    onSuccess: invalidate,
  })
}

export function useBulkValidate() {
  return useMutation({ mutationFn: (ids: number[]) => http.post<BulkValidateResult>('/backups/bulk-validate', { ids }) })
}

export function useTestConnection() {
  return useMutation({ mutationFn: () => http.post<BackupConnection>('/backups/test-connection') })
}

/**
 * The database was replaced, so nothing the app knows is safe to keep: every cached answer (lists, tiles, the signed-in
 * user and their permissions) is refetched. If the restored data no longer has this user, the session refetch answers 401 and the
 * shell sends them to the sign-in page.
 */
export function useRestoreBackup() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: ({ id, createBackupBeforeRestore }: { id: number; createBackupBeforeRestore: boolean }) =>
      http.post<void>(`/backups/${id}/restore`, { createBackupBeforeRestore }),
    onSettled: () => Promise.all([client.invalidateQueries(), client.invalidateQueries({ queryKey: queryKeys.session })]),
  })
}

export function useSaveSchedule() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: ScheduleInput }) =>
      id === undefined ? http.post<ScheduleRow>('/backup-schedules', input) : http.put<ScheduleRow>(`/backup-schedules/${id}`, input),
    onSuccess: invalidate,
  })
}

export function useSetScheduleActive() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, isActive }: { id: number; isActive: boolean }) => http.put<ScheduleRow>(`/backup-schedules/${id}/active`, { isActive }),
    onSuccess: invalidate,
  })
}

export function useDeleteSchedule() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.delete<void>(`/backup-schedules/${id}`), onSuccess: invalidate })
}

export function useRunSchedule() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.post<void>(`/backup-schedules/${id}/run`), onSettled: invalidate })
}

/** The file goes straight from the server to the browser's download bar (a backup can be large: it is never held in memory). */
export function downloadBackup(id: number) {
  const link = document.createElement('a')
  link.href = `/api/v1/backups/${id}/download`
  link.download = ''
  link.style.display = 'none'
  document.body.appendChild(link)
  link.click()
  link.remove()
}
