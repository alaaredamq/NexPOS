import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { queryKeys } from '@/api/queryClient'

type Schemas = components['schemas']
export type UserRow = Schemas['UserDto']
export type UserList = Schemas['UserDtoPagedResult']
export type UserSummary = Schemas['UserSummaryDto']
export type UserCreateInput = Schemas['UserCreateRequest']
export type UserUpdateInput = Schemas['UserUpdateRequest']
export type RoleRow = Schemas['RoleDto']
export type RoleList = Schemas['RoleDtoPagedResult']
export type RoleDetails = Schemas['RoleDetailsDto']
export type RoleInput = Schemas['RoleRequest']

export interface ListParams {
  page: number
  pageSize: number
  sort: string
  dir: 'asc' | 'desc'
  q: string
}

const root = ['users'] as const
export const userKeys = {
  all: root,
  list: (params: ListParams) => [...root, 'list', params] as const,
  summary: [...root, 'summary'] as const,
  user: (id: number) => [...root, 'user', id] as const,
  roles: [...root, 'roles'] as const,
  roleList: (params: ListParams) => [...root, 'roles', 'list', params] as const,
  role: (id: number) => [...root, 'roles', 'role', id] as const,
}

export function useUsers(params: ListParams) {
  return useQuery({
    queryKey: userKeys.list(params),
    queryFn: ({ signal }) => http.get<UserList>('/users', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
  })
}

export function useUserSummary() {
  return useQuery({ queryKey: userKeys.summary, queryFn: ({ signal }) => http.get<UserSummary>('/users/summary', { signal }) })
}

export function useRoles(params: ListParams, enabled = true) {
  return useQuery({
    queryKey: userKeys.roleList(params),
    queryFn: ({ signal }) => http.get<RoleList>('/roles', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
    enabled,
  })
}

const ALL_ROLES: ListParams = { page: 1, pageSize: 200, sort: 'name', dir: 'asc', q: '' }

/** Every role, for the pickers of the user forms (a store has a handful). */
export function useRoleOptions() {
  return useRoles(ALL_ROLES)
}

/** Grants of one role, for the permission matrix. Always fetched fresh when the editor opens. */
export function useRole(id: number | null) {
  return useQuery({
    queryKey: userKeys.role(id ?? 0),
    queryFn: ({ signal }) => http.get<RoleDetails>(`/roles/${id}`, { signal }),
    enabled: id !== null,
    gcTime: 0,
  })
}

/** Role and user changes alter each other's counts and the signed-in user's own permissions, so the session is refreshed too. */
function useInvalidate() {
  const client = useQueryClient()
  return () =>
    Promise.all([client.invalidateQueries({ queryKey: userKeys.all }), client.invalidateQueries({ queryKey: queryKeys.session })])
}

export function useSaveUser() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: UserCreateInput | UserUpdateInput }) =>
      id === undefined ? http.post<UserRow>('/users', input) : http.put<UserRow>(`/users/${id}`, input),
    onSuccess: invalidate,
  })
}

export function useSetUserActive() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, isActive }: { id: number; isActive: boolean }) => http.put<UserRow>(`/users/${id}/active`, { isActive }),
    onSuccess: invalidate,
  })
}

export function useResetPin() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, newPin }: { id: number; newPin: string }) => http.post<void>(`/users/${id}/reset-pin`, { newPin }),
    onSuccess: invalidate,
  })
}

export function useDeleteUser() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.delete<void>(`/users/${id}`), onSuccess: invalidate })
}

export function useSaveRole() {
  const invalidate = useInvalidate()
  return useMutation({
    mutationFn: ({ id, input }: { id?: number; input: RoleInput }) =>
      id === undefined ? http.post<RoleDetails>('/roles', input) : http.put<RoleDetails>(`/roles/${id}`, input),
    onSuccess: invalidate,
  })
}

export function useDeleteRole() {
  const invalidate = useInvalidate()
  return useMutation({ mutationFn: (id: number) => http.delete<void>(`/roles/${id}`), onSuccess: invalidate })
}
