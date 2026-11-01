import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { confirm } from '@/ui/confirm'
import { formatDateTime, formatNumber } from '@/ui/format'
import { IconButton } from '@/ui/IconButton'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { SearchBox } from '@/ui/SearchBox'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import {
  useAlertList,
  useCleanupNotifications,
  useClearNotifications,
  useDeleteAlert,
  useDeleteNotification,
  useDismissAlert,
  useDismissAllAlerts,
  useMarkAllRead,
  useMarkNotificationRead,
  useNotificationList,
  useNotificationSummary,
  type AlertScope,
  type AppAlert,
  type AppNotification,
} from './api'
import { useItemCopy, useItemTarget, useLowStockTarget, useRelativeTime, type ItemTarget } from './copy'
import styles from './notifications.module.css'

type Tab = 'notifications' | 'alerts'
const PAGE_SIZES = [10, 25, 50]

const STATUS_TONE: Record<string, 'danger' | 'warning' | 'success' | 'neutral'> = {
  active: 'warning',
  acknowledged: 'neutral',
  resolved: 'success',
  dismissed: 'neutral',
}

/** `/app/notifications` - the notifications list and (for whoever holds Alerts.Read) the system alerts, with the tiles above them. */
export default function NotificationsPage() {
  const { t } = useTranslation(['notifications', 'common'])
  const canSeeAlerts = useCan(Permissions.Alerts_View)
  const canDismiss = useCan(Permissions.Alerts_Update)
  const canDeleteAlert = useCan(Permissions.Alerts_Delete)
  const copy = useItemCopy()
  const since = useRelativeTime()
  const targetOf = useItemTarget()
  const lowStock = useLowStockTarget()

  const [tab, setTab] = useState<Tab>('notifications')
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [q, setQ] = useState('')
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [scope, setScope] = useState<AlertScope>('open')

  const summary = useNotificationSummary()
  const notifications = useNotificationList({ page, pageSize, q, unread: unreadOnly })
  const alerts = useAlertList({ page, pageSize, q, scope }, canSeeAlerts && tab === 'alerts')

  const markRead = useMarkNotificationRead()
  const markAll = useMarkAllRead()
  const removeNotification = useDeleteNotification()
  const clearAll = useClearNotifications()
  const cleanup = useCleanupNotifications()
  const dismiss = useDismissAlert()
  const dismissAll = useDismissAllAlerts()
  const removeAlert = useDeleteAlert()

  const stats = summary.data
  const showAlerts = canSeeAlerts && tab === 'alerts'
  const list = showAlerts ? alerts : notifications
  const total = list.data?.total ?? 0
  const tile = (value: number | undefined) => (value === undefined ? '-' : formatNumber(value, 0))

  const fail = (error: unknown) => toast.error(error instanceof Error ? error.message : t('common.actionFailed', { ns: 'common' }))
  /** Runs one change and tells the user; the server's localized message lands in a toast if it is refused. */
  const run = async (action: () => Promise<unknown>, done: string) => {
    try {
      await action()
      toast.success(done)
    } catch (error) {
      fail(error)
    }
  }
  const ask = (message: string, action: () => Promise<unknown>, done: string, danger = false) =>
    confirm({
      title: t('Confirm', { ns: 'common' }),
      message,
      confirmLabel: t('Yes', { ns: 'common' }),
      danger,
      onConfirm: async () => {
        await action()
        toast.success(done)
      },
    })

  const changeTab = (next: string) => {
    setTab(next as Tab)
    setPage(1)
    setQ('')
  }

  const rowActions = (item: AppNotification | AppAlert, kind: Tab) =>
    kind === 'notifications' ? (
      <>
        {!(item as AppNotification).isRead && (
          <IconButton
            icon="check"
            label={t('MarkAsRead')}
            onClick={() => void run(() => markRead.mutateAsync(item.id), t('NotificationMarkedAsRead'))}
          />
        )}
        <IconButton
          icon="trash"
          tone="danger"
          label={t('Delete', { ns: 'common' })}
          onClick={() => void ask(t('AreYouSureYouWantToDeleteThisNotification'), () => removeNotification.mutateAsync(item.id), t('NotificationDeleted'), true)}
        />
      </>
    ) : (
      <>
        {canDismiss && (item as AppAlert).status !== 'dismissed' && (item as AppAlert).status !== 'resolved' && (
          <IconButton icon="check" label={t('DismissAlert')} onClick={() => void run(() => dismiss.mutateAsync(item.id), t('AlertDismissed'))} />
        )}
        {canDeleteAlert && (
          <IconButton
            icon="trash"
            tone="danger"
            label={t('Delete', { ns: 'common' })}
            onClick={() => void ask(t('AreYouSureYouWantToDeleteThisAlert'), () => removeAlert.mutateAsync(item.id), t('AlertDeleted'), true)}
          />
        )}
      </>
    )

  const row = (item: AppNotification | AppAlert, kind: Tab) => {
    const isAlert = kind === 'alerts'
    const alert = item as AppAlert
    const note = item as AppNotification
    const unread = isAlert ? alert.status === 'active' : !note.isRead
    const target: ItemTarget = isAlert ? targetOf(alert) : {}
    const title = isAlert ? copy.title(alert) : note.title
    return (
      <li key={`${kind}-${item.id}`} className={styles.row} data-unread={unread} data-kind={item.kind}>
        <span className={styles.dot} aria-hidden="true" />
        <div className={styles.body}>
          <div className={styles.title}>
            {isAlert && alert.productId && (target.to || target.href) ? (
              target.to ? (
                <Link className={styles.link} to={target.to}>
                  {title}
                </Link>
              ) : (
                <a className={styles.link} href={target.href}>
                  {title}
                </a>
              )
            ) : (
              <strong>{title}</strong>
            )}
            {isAlert ? (
              <StatusBadge tone={STATUS_TONE[alert.status] ?? 'neutral'} label={t(`status.${alert.status}`, { defaultValue: alert.status })} />
            ) : (
              <>
                <StatusBadge tone="info" label={t(`type.${note.kind}`, { defaultValue: note.kind })} />
                {(note.priority === 'high' || note.priority === 'critical') && <StatusBadge tone="danger" label={t(`priority.${note.priority}`)} />}
              </>
            )}
          </div>
          <p className={styles.message}>{isAlert ? copy.message(alert) : note.message}</p>
          <time className={styles.time} dateTime={item.createdAt} title={formatDateTime(item.createdAt)}>
            {since(item.createdAt)}
          </time>
        </div>
        <div className={styles.actions}>{rowActions(item, kind)}</div>
      </li>
    )
  }

  const emptyText = showAlerts ? (scope === 'open' ? t('NoActiveAlerts') : t('NoAlertsFound')) : unreadOnly ? t('NoUnreadNotifications') : t('NoNotificationsFound')

  return (
    <div className={styles.page}>
      <PageHeader title={t('NotificationsAndAlerts')}>
        <Button
          variant="secondary"
          loading={cleanup.isPending}
          onClick={() => void ask(t('CleanUpOldNotificationsAndDismissedAlerts'), () => cleanup.mutateAsync(), t('CleanupCompleted'))}
        >
          {t('Cleanup')}
        </Button>
      </PageHeader>

      <div className={styles.tiles}>
        <StatTile label={t('TotalNotifications')} value={tile(stats?.totalNotifications)} sub={stats ? `${formatNumber(stats.unreadNotifications, 0)} ${t('Unread')}` : undefined} />
        {canSeeAlerts && (
          <>
            <StatTile
              label={t('ActiveAlerts')}
              value={tile(stats?.activeAlerts)}
              sub={stats ? t('OpenAlertsCount', { 0: formatNumber(stats.openAlerts, 0) }) : undefined}
              tone={stats && stats.activeAlerts > 0 ? 'danger' : 'default'}
            />
            <StatTile
              label={t('LowStock')}
              value={tile(stats?.lowStockAlerts)}
              sub={stats ? `${formatNumber(stats.outOfStockAlerts, 0)} ${t('OutOfStock')}` : undefined}
            />
          </>
        )}
      </div>

      <section className={styles.card}>
        <div className={styles.toolbar}>
          {canSeeAlerts && (
            <ChipGroup
              aria-label={t('NotificationsAndAlerts')}
              value={tab}
              onChange={changeTab}
              options={[
                { value: 'notifications', label: t('Notifications') },
                { value: 'alerts', label: t('Alerts') },
              ]}
            />
          )}
          {showAlerts ? (
            <ChipGroup
              size="sm"
              aria-label={t('Alerts')}
              value={scope}
              onChange={(next) => {
                setScope(next as AlertScope)
                setPage(1)
              }}
              options={[
                { value: 'open', label: t('ScopeOpen') },
                { value: 'closed', label: t('ScopeClosed') },
                { value: 'all', label: t('ScopeAll') },
              ]}
            />
          ) : (
            <ChipGroup
              size="sm"
              aria-label={t('Notifications')}
              value={unreadOnly ? 'unread' : 'all'}
              onChange={(next) => {
                setUnreadOnly(next === 'unread')
                setPage(1)
              }}
              options={[
                { value: 'all', label: t('ScopeAll') },
                { value: 'unread', label: t('FilterUnread') },
              ]}
            />
          )}
          <div className={styles.search}>
            <SearchBox
              value={q}
              onSearch={(next) => {
                setQ(next)
                setPage(1)
              }}
              placeholder={t('Search')}
              aria-label={t('Search')}
            />
          </div>
          <div className={styles.bulk}>
            {showAlerts ? (
              <>
                {lowStock &&
                  (lowStock.to ? (
                    <Link className={styles.textLink} to={lowStock.to}>
                      {t('ViewLowStockItems')}
                    </Link>
                  ) : (
                    <a className={styles.textLink} href={lowStock.href}>
                      {t('ViewLowStockItems')}
                    </a>
                  ))}
                {canDismiss && (
                  <Button
                    disabled={!stats || stats.openAlerts === 0}
                    onClick={() => void ask(t('DismissAllActiveAlerts'), () => dismissAll.mutateAsync(), t('AllAlertsDismissed'))}
                  >
                    {t('DismissAll')}
                  </Button>
                )}
              </>
            ) : (
              <>
                <Button
                  disabled={!stats || stats.unreadNotifications === 0}
                  loading={markAll.isPending}
                  onClick={() => void ask(t('MarkAllNotificationsAsRead'), () => markAll.mutateAsync(), t('AllMarkedRead'))}
                >
                  {t('MarkAllRead')}
                </Button>
                <Button
                  variant="danger"
                  disabled={!stats || stats.totalNotifications === 0}
                  onClick={() => void ask(t('ClearAllNotifications'), () => clearAll.mutateAsync(), t('AllCleared'), true)}
                >
                  {t('ClearAll')}
                </Button>
              </>
            )}
          </div>
        </div>

        {list.isPending ? (
          <Loader fullSection label={t('Loading', { ns: 'common' })} />
        ) : list.isError ? (
          <div className={styles.empty} role="alert">
            <p>{list.error.message}</p>
            <Button onClick={() => void list.refetch()}>{t('Retry')}</Button>
          </div>
        ) : list.data.items.length === 0 ? (
          <div className={styles.empty}>{emptyText}</div>
        ) : (
          <ul className={styles.list} aria-label={showAlerts ? t('Alerts') : t('Notifications')}>
            {showAlerts ? alerts.data?.items.map((item) => row(item, 'alerts')) : notifications.data?.items.map((item) => row(item, 'notifications'))}
          </ul>
        )}

        <div className={styles.footer}>
          <Pagination page={page} pageSize={pageSize} total={total} pageSizes={PAGE_SIZES} onPageChange={setPage} onPageSizeChange={(size) => { setPageSize(size); setPage(1) }} />
        </div>
      </section>
    </div>
  )
}
