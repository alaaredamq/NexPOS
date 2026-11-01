import { keepPreviousData, useMutation, useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import type { components } from '@/api/schema'
import { http } from '@/api/http'
import { queryKeys } from '@/api/queryClient'

type Schemas = components['schemas']
export type AppNotification = Schemas['NotificationRowDto']
export type AppAlert = Schemas['AlertRowDto']
export type FeedItem = Schemas['FeedItemDto']
export type Feed = Schemas['NotificationFeedDto']
export type NotificationSummary = Schemas['NotificationSummaryDto']
export type NotificationList = Schemas['NotificationRowDtoPagedResult']
export type AlertList = Schemas['AlertRowDtoPagedResult']

/** Alert scopes of the alerts tab (`open` = active or acknowledged). */
export type AlertScope = 'open' | 'closed' | 'all'

export interface NotificationListParams {
  page: number
  pageSize: number
  q: string
  unread: boolean
}

export interface AlertListParams {
  page: number
  pageSize: number
  q: string
  scope: AlertScope
}

/** How often the bell and the page look for news. There is no push channel (no SignalR): a 30 s poll, paused while the tab is hidden. */
export const FEED_REFRESH_MS = 30_000
export const BELL_ITEMS = 5

const root = ['notifications'] as const
export const notificationKeys = {
  all: root,
  feed: [...root, 'feed'] as const,
  summary: [...root, 'summary'] as const,
  list: (params: NotificationListParams) => [...root, 'list', params] as const,
  alerts: (params: AlertListParams) => [...root, 'alerts', params] as const,
}

/** What the bell shows: the newest items and the unread total (alerts only for someone who may see them - the server decides). */
export function useFeed() {
  return useQuery({
    queryKey: notificationKeys.feed,
    queryFn: ({ signal }) => http.get<Feed>('/notifications/feed', { query: { take: BELL_ITEMS }, signal }),
    refetchInterval: FEED_REFRESH_MS,
    refetchOnWindowFocus: true,
    staleTime: 10_000,
    // A failed poll (offline, session ended) is silent: the next tick tries again, the global 401 handler does the rest.
    retry: false,
  })
}

export function useNotificationSummary() {
  return useQuery({
    queryKey: notificationKeys.summary,
    queryFn: ({ signal }) => http.get<NotificationSummary>('/notifications/summary', { signal }),
    refetchInterval: FEED_REFRESH_MS,
  })
}

export function useNotificationList(params: NotificationListParams) {
  return useQuery({
    queryKey: notificationKeys.list(params),
    queryFn: ({ signal }) => http.get<NotificationList>('/notifications', { query: { ...params, unread: params.unread || undefined }, signal }),
    placeholderData: keepPreviousData,
    refetchInterval: FEED_REFRESH_MS,
  })
}

export function useAlertList(params: AlertListParams, enabled: boolean) {
  return useQuery({
    queryKey: notificationKeys.alerts(params),
    queryFn: ({ signal }) => http.get<AlertList>('/notifications/alerts', { query: { ...params }, signal }),
    placeholderData: keepPreviousData,
    refetchInterval: FEED_REFRESH_MS,
    enabled,
  })
}

/** Everything that shows a count or a list is stale after a change - the bell, the tiles, both lists, and the session's unread badge. */
export function refreshNotifications(client: QueryClient) {
  return Promise.all([client.invalidateQueries({ queryKey: notificationKeys.all }), client.invalidateQueries({ queryKey: queryKeys.session })])
}

function useChange<TVariables = void>(request: (variables: TVariables) => Promise<unknown>) {
  const client = useQueryClient()
  return useMutation({ mutationFn: request, onSuccess: () => refreshNotifications(client) })
}

export const useMarkNotificationRead = () => useChange((id: number) => http.post(`/notifications/${id}/read`))
export const useDeleteNotification = () => useChange((id: number) => http.delete(`/notifications/${id}`))
export const useMarkAllRead = () => useChange(() => http.post('/notifications/read-all'))
export const useClearNotifications = () => useChange(() => http.delete('/notifications'))
export const useCleanupNotifications = () => useChange(() => http.post('/notifications/cleanup'))
export const useDismissAlert = () => useChange((id: number) => http.post(`/notifications/alerts/${id}/dismiss`))
export const useDismissAllAlerts = () => useChange(() => http.post('/notifications/alerts/dismiss-all'))
export const useDeleteAlert = () => useChange((id: number) => http.delete(`/notifications/alerts/${id}`))

/** A click on a bell row: a notification is marked read, an alert is acknowledged. */
export const useMarkFeedItemRead = () =>
  useChange(({ source, id }: { source: string; id: number }) => http.post(`/notifications/feed/${source}/${id}/read`))
