import { useEffect, useRef, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useTranslation } from 'react-i18next'
import { useSession } from '@/core/auth/useSession'
import { NavIcon } from '@/shell/NavIcon'
import { BELL_ICON } from '@/shell/navIcons'
import { flattenNav } from '@/shell/nav'
import { useFeed, useMarkAllRead, useMarkFeedItemRead, type FeedItem } from '../api'
import { useItemCopy, useItemTarget, useRelativeTime, type ItemTarget } from '../copy'
import { BellToasts, useFreshToasts } from './BellToasts'
import styles from './bell.module.css'

/**
 * The top-bar bell: unread badge, a dropdown with the newest items, and pop-up toasts for new unread ones. Everything comes from
 * `GET /notifications/feed` (polled every 30 s, and when the window regains focus); until the first answer the badge shows the
 * session's count. Clicking an item marks it read (an alert is acknowledged) and opens what it is about.
 */
export function NotificationBell() {
  const { t } = useTranslation('notifications', { useSuspense: false })
  const navigate = useNavigate()
  const { unreadNotifications, nav } = useSession()
  const feed = useFeed()
  const markRead = useMarkFeedItemRead()
  const markAll = useMarkAllRead()
  const copy = useItemCopy()
  const since = useRelativeTime()
  const targetOf = useItemTarget()
  const [open, setOpen] = useState(false)
  const wrap = useRef<HTMLDivElement>(null)
  const [toasts, dismissToast] = useFreshToasts(feed.data?.items, open)

  const page = flattenNav(nav).find((item) => item.key === 'Notifications')

  useEffect(() => {
    if (!open) return
    const close = (event: Event) => {
      if (event instanceof KeyboardEvent ? event.key === 'Escape' : !wrap.current?.contains(event.target as Node)) setOpen(false)
    }
    document.addEventListener('mousedown', close)
    document.addEventListener('keydown', close)
    return () => {
      document.removeEventListener('mousedown', close)
      document.removeEventListener('keydown', close)
    }
  }, [open])

  if (!page) return null

  const unread = feed.data?.unreadCount ?? unreadNotifications
  const items = feed.data?.items ?? []
  const label = unread > 0 ? `${t('Notifications')} (${t('Shell_Unread', { ns: 'common', 0: unread })})` : t('Notifications')

  const go = (target: ItemTarget) => {
    if (target.to) void navigate(target.to)
    else if (target.href) window.location.assign(target.href)
  }

  const openItem = (item: FeedItem) => {
    setOpen(false)
    // Fire and forget: the page the item leads to should not wait for the acknowledgement.
    if (!item.isRead) markRead.mutate({ source: item.source, id: item.id })
    go(targetOf(item))
  }

  return (
    <div className={styles.wrap} ref={wrap}>
      <button
        type="button"
        className={styles.bellBtn}
        aria-label={label}
        title={label}
        aria-haspopup="true"
        aria-expanded={open}
        onClick={() => setOpen((value) => !value)}
      >
        <NavIcon d={BELL_ICON} />
        {unread > 0 && <span className={styles.badge}>{unread > 9 ? '9+' : unread}</span>}
      </button>

      {open && (
        <div className={styles.panel} role="dialog" aria-label={t('Notifications')}>
          <div className={styles.panelHead}>
            <strong>{t('Notifications')}</strong>
            {unread > 0 && (
              <button type="button" className={styles.linkBtn} disabled={markAll.isPending} onClick={() => markAll.mutate()}>
                {t('markAllRead')}
              </button>
            )}
          </div>

          <div className={styles.list}>
            {items.length === 0 ? (
              <div className={styles.empty}>{t('empty')}</div>
            ) : (
              items.map((item) => (
                <button key={item.key} type="button" className={styles.item} data-unread={!item.isRead} data-kind={item.kind} onClick={() => openItem(item)}>
                  <span className={styles.dot} aria-hidden="true" />
                  <span className={styles.itemBody}>
                    <span className={styles.itemTitle}>{copy.title(item)}</span>
                    <span className={styles.itemMessage}>{copy.message(item)}</span>
                    <span className={styles.itemTime}>{since(item.createdAt)}</span>
                  </span>
                </button>
              ))
            )}
          </div>

          <div className={styles.panelFoot}>
            {page.migrated ? (
              <Link to={page.routerPath} className={styles.linkBtn} onClick={() => setOpen(false)}>
                {t('ViewAll')}
              </Link>
            ) : (
              <a href={page.href} className={styles.linkBtn}>
                {t('ViewAll')}
              </a>
            )}
          </div>
        </div>
      )}

      <BellToasts toasts={toasts} onDismiss={dismissToast} onOpen={openItem} />
    </div>
  )
}
