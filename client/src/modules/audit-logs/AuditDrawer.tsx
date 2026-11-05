import { useEffect, useId, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { IconButton } from '@/ui/IconButton'
import { Skeleton } from '@/ui/Skeleton'
import { toast } from '@/ui/toast'
import { useAuditEntry, type AuditEntryDetails, type AuditParams } from './api'
import { AreaBadge, CategoryBadge, formatStamp, OutcomeBadges } from './badges'
import styles from './auditLogs.module.css'

const FOCUSABLE = 'a[href],button:not([disabled]),input:not([disabled]),select:not([disabled]),textarea:not([disabled]),[tabindex]:not([tabindex="-1"])'

interface DrawerProps {
  id: number
  onClose: () => void
  /** Replace the page's filters (the drawer's "everything in this request / by this user ..." buttons). The page closes the drawer. */
  onDrill: (patch: Partial<AuditParams>) => void
}

/**
 * The details of one entry, sliding in from the inline-end edge: who, what, where, the field-by-field changes, the raw JSON
 * and the way on to everything else the same request / record / user wrote. A dialog in the accessibility sense (focus is trapped,
 * Escape closes, focus returns to the row), shown as a side panel so the table stays visible behind it.
 */
export function AuditDrawer({ id, onClose, onDrill }: DrawerProps) {
  const { t } = useTranslation('auditLogs')
  const titleId = useId()
  const panel = useRef<HTMLElement>(null)
  const query = useAuditEntry(id)
  const onCloseRef = useRef(onClose)
  useEffect(() => {
    onCloseRef.current = onClose
  })

  useEffect(() => {
    const before = document.activeElement as HTMLElement | null
    panel.current?.focus()
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        if (e.defaultPrevented) return
        e.stopPropagation()
        onCloseRef.current()
        return
      }
      if (e.key !== 'Tab' || !panel.current) return
      const items = Array.from(panel.current.querySelectorAll<HTMLElement>(FOCUSABLE))
      if (items.length === 0) return
      const first = items[0]!
      const last = items[items.length - 1]!
      const active = document.activeElement
      if (e.shiftKey && (active === first || active === panel.current)) {
        e.preventDefault()
        last.focus()
      } else if (!e.shiftKey && active === last) {
        e.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('keydown', onKeyDown)
      before?.focus?.()
    }
  }, [])

  return createPortal(
    <div className={styles.overlay} onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <aside ref={panel} className={styles.drawer} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1}>
        <header className={styles.drawerHead}>
          <h2 id={titleId} className={styles.drawerTitle}>
            {t('Detail_Title')} <span className={styles.muted}>#{id}</span>
          </h2>
          <IconButton icon="close" label={t('Detail_Close')} onClick={onClose} />
        </header>

        {query.isPending && (
          <div className={styles.drawerBody} aria-busy="true">
            <Skeleton width="60%" />
            <Skeleton width="90%" />
            <Skeleton width="40%" />
          </div>
        )}
        {query.isError && (
          <div className={styles.drawerBody} role="alert">
            {isApiError(query.error) && query.error.status !== 404 ? query.error.message : t('Msg_DetailFailed')}
          </div>
        )}
        {query.data && <EntryBody details={query.data} onDrill={onDrill} />}
      </aside>
    </div>,
    document.body,
  )
}

function EntryBody({ details, onDrill }: { details: AuditEntryDetails; onDrill: (patch: Partial<AuditParams>) => void }) {
  const { t } = useTranslation('auditLogs')
  const e = details.entry
  const isRequest = e.category === 'Request'
  const who = e.user === 'System' ? t('Msg_System') : (e.user ?? '—')

  const record = [e.entityType, e.entityId ? `#${e.entityId}` : '', e.label ? `· ${e.label}` : ''].filter(Boolean).join(' ')

  const copyRequestId = async () => {
    if (!details.correlationId) return
    try {
      await navigator.clipboard.writeText(details.correlationId)
      toast.success(t('Detail_Copied'))
    } catch {
      toast.error(t('Msg_CopyFailed'))
    }
  }

  const meta: [string, string | number | null | undefined][] = [
    [t('Detail_RequestId'), details.correlationId],
    [t('Detail_Method'), details.httpMethod],
    [t('Detail_Path'), details.path],
    [t('Detail_Status'), e.statusCode],
    [t('Detail_Duration'), e.durationMs == null ? null : `${e.durationMs} ms`],
    [t('Detail_Ip'), details.ipAddress],
    [t('Detail_Machine'), details.machine],
    [t('Detail_Version'), details.appVersion],
    [t('Detail_Source'), details.source],
    [t('Detail_Agent'), details.userAgent],
  ]

  return (
    <>
      <div className={styles.drawerBody}>
        <div className={styles.summary}>
          <div className={styles.badges}>
            <AreaBadge area={e.area} />
            <CategoryBadge category={e.category} />
            <code className={styles.action}>{e.action}</code>
            <OutcomeBadges outcome={e.outcome} severity={e.severity} />
          </div>
          {/* dir="auto": the text is whatever language the data was typed in. */}
          <p className={styles.summaryText} dir="auto">
            {e.description}
          </p>
        </div>

        <div className={styles.facts}>
          <Fact label={t('Detail_When')}>
            <bdi dir="ltr">{formatStamp(e.time)}</bdi>
          </Fact>
          <Fact label={t('Detail_Who')}>
            <bdi>{who}</bdi>
            {e.role ? ` · ${e.role}` : ''}
          </Fact>
          {/* A request isn't about a record ("Http" is only the bucket it is filed under). */}
          {!isRequest && (
            <Fact label={t('Detail_Record')}>
              <bdi>{record || '—'}</bdi>
            </Fact>
          )}
        </div>

        <Changes details={details} />

        {details.additional && (
          <section>
            <h3 className={styles.section}>{t('Detail_Extra')}</h3>
            <pre className={styles.json} dir="ltr">
              {details.additional}
            </pre>
          </section>
        )}

        <section>
          <h3 className={styles.section}>{t('Detail_Where')}</h3>
          <dl className={styles.meta}>
            {meta
              .filter(([, value]) => value !== null && value !== undefined && value !== '')
              .map(([label, value]) => (
                <div key={label} className={styles.metaRow}>
                  <dt>{label}</dt>
                  <dd>
                    <bdi dir="ltr">{value}</bdi>
                  </dd>
                </div>
              ))}
          </dl>
        </section>
      </div>

      <footer className={styles.drawerFoot}>
        {details.correlationId && (
          <Button size="md" onClick={() => onDrill({ ...CLEAR, request: details.correlationId ?? '', dir: 'asc' })}>
            {t('Detail_ThisRequest', { 0: details.rowsInSameRequest })}
          </Button>
        )}
        {e.entityId && !isRequest && (
          <Button onClick={() => onDrill({ ...CLEAR, entityType: e.entityType, entityId: String(e.entityId) })}>{t('Detail_RecordHistory')}</Button>
        )}
        {e.user && <Button onClick={() => onDrill({ ...CLEAR, user: e.userId ? String(e.userId) : `name:${e.user}` })}>{t('Detail_ByUser')}</Button>}
        {details.correlationId && (
          <Button variant="ghost" onClick={() => void copyRequestId()}>
            {t('Detail_Copy')}
          </Button>
        )}
      </footer>
    </>
  )
}

/** A drill-down starts from a clean slate: "everything by this user" must not also be limited by yesterday's search. */
const CLEAR: Partial<AuditParams> = {
  q: '',
  from: '',
  to: '',
  user: '',
  area: '',
  category: '',
  outcome: '',
  severity: '',
  entityType: '',
  entityId: '',
  action: '',
  request: '',
  dir: 'desc',
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className={styles.fact}>
      <span>{label}</span>
      <strong dir="auto">{children}</strong>
    </div>
  )
}

function Changes({ details }: { details: AuditEntryDetails }) {
  const { t } = useTranslation('auditLogs')
  const changes = details.changes

  // "What changed" only means something for a change to a record. A request, a sign-in or an error has no old/new
  // values, so "no field-level changes were recorded" there is just noise: leave the section out.
  if (changes.length === 0 && details.entry.category !== 'Data') return null

  return (
    <section>
      <h3 className={styles.section}>{t('Detail_Changes')}</h3>
      {changes.length === 0 ? (
        <p className={styles.muted}>{t('Detail_NoChanges')}</p>
      ) : (
        <ChangeTable changes={changes} />
      )}
    </section>
  )
}

function ChangeTable({ changes }: { changes: AuditEntryDetails['changes'] }) {
  const { t } = useTranslation('auditLogs')
  const hasBefore = changes.some((c) => c.oldValue != null)
  const hasAfter = changes.some((c) => c.newValue != null)
  const isDiff = hasBefore && hasAfter
  const value = (v: string | null | undefined) => (v == null ? '∅' : v)

  return (
    <div className={styles.tableScroll}>
      <table className={styles.diff}>
        <thead>
          <tr>
            <th scope="col">{t('Detail_Field')}</th>
            {isDiff ? (
              <>
                <th scope="col">{t('Detail_Before')}</th>
                <th scope="col">{t('Detail_After')}</th>
              </>
            ) : (
              <th scope="col">{hasBefore ? t('Detail_Before') : t('Detail_Value')}</th>
            )}
          </tr>
        </thead>
        <tbody>
          {changes.map((c) => (
            <tr key={c.field}>
              <th scope="row" className={styles.field}>
                <bdi dir="ltr">{c.field}</bdi>
              </th>
              {isDiff ? (
                <>
                  <td className={styles.oldValue} dir="auto">{value(c.oldValue)}</td>
                  <td className={styles.newValue} dir="auto">{value(c.newValue)}</td>
                </>
              ) : (
                <td dir="auto">{value(hasBefore ? c.oldValue : c.newValue)}</td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
