import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { queryKeys } from '@/api/queryClient'

export type Profile = components['schemas']['ProfileDto']
export type ChangePinInput = components['schemas']['ChangePinRequest']

const profileKey = ['account', 'profile'] as const

export function useProfile() {
  return useQuery({ queryKey: profileKey, queryFn: ({ signal }) => http.get<Profile>('/account/profile', { signal }) })
}

/** After a change the session is refreshed too: its `mustChangePin` flag is what holds the user on this page. */
export function useChangePin() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (input: ChangePinInput) => http.post<void>('/account/change-pin', input),
    onSuccess: () => Promise.all([client.invalidateQueries({ queryKey: profileKey }), client.invalidateQueries({ queryKey: queryKeys.session })]),
  })
}
