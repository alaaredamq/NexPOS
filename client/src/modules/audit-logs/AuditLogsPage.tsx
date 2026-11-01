import { useMemo, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { useSearchParams } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { DataTable, type Column, type SortState } from '@/ui/DataTable'
import { formatNumber } from '@/ui/format'
import { PageHeader } from '@/ui/PageHeader'
import { Pagination } from '@/ui/Pagination'
import { StatTile } from '@/ui/StatTile'
import { toast } from '@/ui/toast'
import { saveDownload } from '../_shared/saveDownload'
import { AuditDrawer } from './AuditDrawer'
import { AuditFilters } from './AuditFilters'
import { auditApi, auditKeys, useAuditEntries, useAuditSummary, type AuditEntry, type AuditParams } from './api'
import { CategoryBadge, formatStamp, OutcomeBadges } from './badges'
import { EMPTY_FILTERS, hasFilters, PAGE_SIZES, readParams, writeParams } from './listParams'
import styles from './auditLogs.module.css'

/** Rows are two lines tall (name over role); a long page is windowed so 200 rows stay light. */
const ROW_HEIGHT = 58
const VIRTUALIZE_FROM = 100

/** `/app/audit-logs` - the read-only audit trail: tiles, filters mirrored in the URL, a server-paged table, a details drawer, CSV export. */
export default function AuditLogsPage() {
  const { t } = useTranslation('auditLogs')
  const client = useQueryClient()
  const [search, setSearch] = useSearchParams()
  const params = useMemo(() => readParams(search), [search])
  const list = useAuditEntries(params)
  const summary = useAuditSummary()
  const [exporting, setExporting] = useState(false)

  const write = (next: AuditParams) => setSearch(writeParams(next), { replace: true })
  /** Any filter or order change goes back to page 1; the open entry stays open. */
  const update = (patch: Partial<AuditParams>) => write({ ...params, page: 1, ...patch })
  /** Opening an entry is navigation (the back button closes it); everything else replaces the history entry. */
  const openEntry = (entry: number | null) => write({ ...params, entry })
  const reset = () => write({ ...params, ...EMPTY_FILTERS, dir: 'desc', page: 1 })

  const stats = summary.data
  const sort: SortState = { key: 'time', dir: params.dir }

  const refresh = () => void client.invalidateQueries({ queryKey: auditKeys.all })

  const onExport = async () => {
    setExporting(true)
    try {
      saveDownload(await auditApi.export(params), 'nexpos-audit.csv')
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('Msg_ExportFailed'))
    } finally {
      setExporting(false)
    }
  }

  const columns: Column<AuditEntry>[] = [
    {
      key: 'time',
      header: t('Col_Time'),
      sortable: true,
      required: true,
      cell: (e) => <bdi dir="ltr">{formatStamp(e.time)}</bdi>,
    },
    {
      key: 'user',
      header: t('Col_User'),
      required: true,
      cell: (e) =>
        e.user ? (
          <div>
            <div className={styles.userName}>
              <bdi>{e.user === 'System' ? t('Msg_System') : e.user}</bdi>
            </div>
            {e.role && <div className={styles.sub}>{e.role}</div>}
          </div>
        ) : (
          <span className={styles.muted}>—</span>
        ),
    },
    { key: 'category', header: t('Col_Type'), hideBelow: 900, cell: (e) => <CategoryBadge category={e.category} /> },
    { key: 'action', header: t('Col_Action'), hideBelow: 1100, cell: (e) => <code className={styles.action}>{e.action}</code> },
    {
      key: 'record',
      header: t('Col_Record'),
      hideBelow: 1000,
      cell: (e) =>
        e.category === 'Request' ? null : (
          <div>
            <div>
              <bdi>{e.entityType}</bdi> {e.entityId ? <span className={styles.muted}>#{e.entityId}</span> : null}
            </div>
            {e.label && (
              <div className={styles.sub} title={e.label}>
                <bdi>{e.label}</bdi>
              </div>
            )}
          </div>
        ),
    },
    {
      key: 'description',
      header: t('Col_Description'),
      required: true,
      cell: (e) => (
        <div className={styles.description} dir="auto" title={e.description ?? undefined}>
          {e.description}
        </div>
      ),
    },
    { key: 'outcome', header: t('Col_Result'), required: true, cell: (e) => <OutcomeBadges outcome={e.outcome} severity={e.severity} /> },
  ]

  const sinceText = stats?.oldestUtc ? `${t('Stat_Since')} ${formatStamp(stats.oldestUtc).slice(0, 10)}` : t('Stat_AllTime')
  const tile = (n: number | undefined) => (n === undefined ? '–' : formatNumber(n, 0))

  return (
    <div className={styles.page}>
      <PageHeader title={t('AuditLog_Title')} count={list.data ? formatNumber(list.data.total, 0) : undefined}>
        <Button onClick={refresh}>{t('Btn_Refresh')}</Button>
        <Button loading={exporting} onClick={() => void onExport()}>
          {t('Btn_Export')}
        </Button>
      </PageHeader>
      <p className={styles.subtitle}>{t('AuditLog_Subtitle')}</p>

      <div className={styles.tiles}>
        <StatTile label={t('Stat_Total')} value={tile(stats?.total)} sub={stats ? sinceText : undefined} />
        <StatTile label={t('Stat_Last24h')} value={tile(stats?.last24Hours)} sub={t('Stat_Events')} />
        <StatTile
          label={t('Stat_Failures24h')}
          value={tile(stats?.failures24Hours)}
          sub={t('Stat_NeedsAttention')}
          tone={stats ? (stats.failures24Hours > 0 ? 'danger' : 'success') : 'default'}
        />
        <StatTile label={t('Stat_Security24h')} value={tile(stats?.security24Hours)} sub={t('Stat_Logins')} />
      </div>

      <AuditFilters params={params} onChange={update} onReset={reset} />

      <DataTable<AuditEntry>
        caption={t('AuditLog_Title')}
        columns={columns}
        rows={list.data?.items ?? []}
        rowKey={(e) => e.id}
        loading={list.isPending}
        refreshing={list.isFetching && !list.isPending}
        error={list.isError ? (isApiError(list.error) ? list.error.message : t('Msg_LoadFailed')) : null}
        onRetry={() => void list.refetch()}
        emptyTitle={t('Tbl_Empty')}
        emptyAction={hasFilters(params) ? <Button onClick={reset}>{t('Btn_Reset')}</Button> : undefined}
        sort={sort}
        // The only sort there is, so a click on Time just flips oldest-first / newest-first (the table's own asc -> desc -> off cycle would need two clicks to leave the default).
        onSortChange={() => update({ dir: params.dir === 'desc' ? 'asc' : 'desc' })}
        onRowClick={(e) => openEntry(e.id)}
        selectedKey={params.entry}
        virtualize={params.pageSize >= VIRTUALIZE_FROM ? { rowHeight: ROW_HEIGHT, maxHeight: 640 } : undefined}
        footer={
          <Pagination
            page={params.page}
            pageSize={params.pageSize}
            total={list.data?.total ?? 0}
            pageSizes={PAGE_SIZES}
            onPageChange={(page) => write({ ...params, page })}
            onPageSizeChange={(pageSize) => update({ pageSize })}
          />
        }
      />

      {params.entry !== null && (
        <AuditDrawer
          id={params.entry}
          onClose={() => openEntry(null)}
          // A drill-down replaces the filters and closes the drawer; the table behind it is already the answer.
          onDrill={(patch) => write({ ...params, page: 1, ...patch, entry: null })}
        />
      )}
    </div>
  )
}
