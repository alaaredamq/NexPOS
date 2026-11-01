import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { Button } from '@/ui/Button'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDateTime, formatNumber } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { StatTile } from '@/ui/StatTile'
import { StatusBadge } from '@/ui/StatusBadge'
import { useCurrentDrawer, type CashDrop } from './api'
import { CashDropModal } from './CashDropModal'
import { money } from './money'
import { dropReasonLabel } from './reasons'
import styles from './cashDrawer.module.css'


/** `/app/cash-drawer` - the signed-in user's drawer: running figures of the open session, cash drops, close, history. */
export default function DrawerPage() {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const navigate = useNavigate()
  const canEdit = useCan(Permissions.Sales_Edit)
  const query = useCurrentDrawer()
  const [dropping, setDropping] = useState(false)

  if (query.isPending) return <Loader fullSection label={t('Loading', { ns: 'cashDrawer' })} />
  if (query.isError) {
    return (
      <div className={styles.page}>
        <div className={styles.card} role="alert">
          {query.error.message}
          <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
        </div>
      </div>
    )
  }

  const session = query.data.session
  const dropColumns: Column<CashDrop>[] = [
    { key: 'time', header: t('Time', { ns: 'cashDrawer' }), cell: (d) => formatDateTime(d.droppedAt) },
    { key: 'amount', header: t('Amount', { ns: 'cashDrawer' }), align: 'end', cell: (d) => money(d.amount) },
    { key: 'reason', header: t('Reason', { ns: 'cashDrawer' }), cell: (d) => dropReasonLabel(t, d.reason) },
    { key: 'notes', header: t('Notes', { ns: 'cashDrawer' }), hideBelow: 700, cell: (d) => (d.notes ? <bdi>{d.notes}</bdi> : '-') },
  ]

  return (
    <div className={styles.page}>
      <PageHeader
        title={t('CashDrawerManagement', { ns: 'cashDrawer' })}
        primaryActionLabel={session ? undefined : t('OpenCashDrawer', { ns: 'cashDrawer' })}
        onPrimaryAction={session ? undefined : () => void navigate('/cash-drawer/open')}
      >
        {session && canEdit && (
          <>
            <Button onClick={() => setDropping(true)}>{t('CashDrop', { ns: 'cashDrawer' })}</Button>
            <Button variant="primary" onClick={() => void navigate(`/cash-drawer/close/${session.id}`)}>
              {t('CloseDrawer', { ns: 'cashDrawer' })}
            </Button>
          </>
        )}
        <Button variant="ghost" onClick={() => void navigate('/cash-drawer/history')}>
          {t('History', { ns: 'cashDrawer' })}
        </Button>
      </PageHeader>

      {session ? (
        <>
          <section className={`${styles.card} ${styles.cardOpen}`} aria-labelledby="current-session">
            <h2 className={styles.cardTitle} id="current-session">
              {t('CurrentOpenSession', { ns: 'cashDrawer' })} <StatusBadge tone="success" label={t('Open', { ns: 'cashDrawer' })} />
            </h2>
            <dl className={styles.facts}>
              <div>
                <dt>{t('OpenedAt', { ns: 'cashDrawer' })}</dt>
                <dd>{formatDateTime(session.openedAt)}</dd>
              </div>
              <div>
                <dt>{t('OpeningCash', { ns: 'cashDrawer' })}</dt>
                <dd>{money(session.openingCash)}</dd>
              </div>
              <div>
                <dt>{t('SessionId', { ns: 'cashDrawer' })}</dt>
                <dd>#{session.id}</dd>
              </div>
              <div>
                <dt>{t('Cashier', { ns: 'cashDrawer' })}</dt>
                <dd>
                  <bdi>{session.cashierName}</bdi>
                </dd>
              </div>
            </dl>
          </section>

          <div className={styles.tiles}>
            <StatTile label={t('TotalSales', { ns: 'cashDrawer' })} value={money(session.totalSales)} tone="success" />
            <StatTile label={t('Transactions', { ns: 'cashDrawer' })} value={formatNumber(session.totalTransactions, 0)} />
            <StatTile label={t('ExpectedCash', { ns: 'cashDrawer' })} value={money(session.expectedCash)} tone="info" />
            <StatTile label={t('CashDrops', { ns: 'cashDrawer' })} value={money(session.totalCashDrops)} />
          </div>

          {session.cashDrops.length > 0 && (
            <DataTable<CashDrop>
              caption={t('CashDrops', { ns: 'cashDrawer' })}
              columns={dropColumns}
              rows={session.cashDrops}
              rowKey={(d) => d.id}
              refreshing={query.isFetching}
            />
          )}
        </>
      ) : (
        <div className={`${styles.card} ${styles.empty}`}>
          <h2>{t('NoOpenCashDrawerSession', { ns: 'cashDrawer' })}</h2>
          <p className={styles.muted}>{t('YouNeedToOpenACashDrawerToStartProcessingSales', { ns: 'cashDrawer' })}</p>
          <Button variant="primary" size="lg" onClick={() => void navigate('/cash-drawer/open')}>
            {t('OpenCashDrawer', { ns: 'cashDrawer' })}
          </Button>
        </div>
      )}

      {dropping && session && <CashDropModal sessionId={session.id} onClose={() => setDropping(false)} />}
    </div>
  )
}
