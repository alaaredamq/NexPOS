import type { ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate, useParams } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { formatDateTime, formatNumber } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { NotFoundPage } from '@/shell/pages/pages'
import { useZReport, type ZReport } from './api'
import { differenceOf } from './drawerMath'
import { money, signedMoney } from './money'
import { PrintMenu } from './PrintMenu'
import { dropReasonLabel } from './reasons'
import styles from './cashDrawer.module.css'


function Row({ label, value, total }: { label: string; value: ReactNode; total?: boolean }) {
  return (
    <div className={total ? `${styles.row} ${styles.rowTotal}` : styles.row}>
      <dt>{label}</dt>
      <dd className="tabular-nums">{value}</dd>
    </div>
  )
}

/** `/app/cash-drawer/z-report/:id` - the end-of-day report of a closed session, printable and downloadable. */
export default function ZReportPage() {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const navigate = useNavigate()
  const id = Number(useParams().id)
  const query = useZReport(id)

  if (!Number.isInteger(id) || id < 1 || (isApiError(query.error) && query.error.status === 404)) return <NotFoundPage />
  if (query.isPending) return <Loader fullSection label={t('Loading', { ns: 'cashDrawer' })} />
  if (query.isError) {
    const stillOpen = isApiError(query.error) && query.error.code === 'session_still_open'
    return (
      <div className={styles.page}>
        <div className={`${styles.narrow} ${styles.card}`} role="alert">
          {query.error.message}
          <div className={styles.actions}>
            {stillOpen ? (
              <Button variant="primary" onClick={() => void navigate(`/cash-drawer/close/${id}`)}>
                {t('CloseCashDrawer', { ns: 'cashDrawer' })}
              </Button>
            ) : (
              <Button onClick={() => void query.refetch()}>{t('common.retry', { ns: 'common' })}</Button>
            )}
            <Button variant="ghost" onClick={() => void navigate('/cash-drawer/history')}>
              {t('SessionHistory', { ns: 'cashDrawer' })}
            </Button>
          </div>
        </div>
      </div>
    )
  }

  const report: ZReport = query.data
  const difference = differenceOf(report.expectedCash, report.actualCash)
  const hours = ((new Date(report.closedAt).getTime() - new Date(report.openedAt).getTime()) / 3_600_000).toFixed(1)

  return (
    <div className={styles.page}>
      <div className={styles.noPrint}>
        <PageHeader title={t('ZReport', { ns: 'cashDrawer' })}>
          <Button variant="ghost" onClick={() => void navigate('/cash-drawer')}>{t('CashDrawerManagement', { ns: 'cashDrawer' })}</Button>
          <Button variant="ghost" onClick={() => void navigate('/cash-drawer/history')}>{t('SessionHistory', { ns: 'cashDrawer' })}</Button>
        </PageHeader>
      </div>
      <PrintMenu sessionId={report.sessionId} />

      <article className={styles.report} aria-label={t('ZReport', { ns: 'cashDrawer' })}>
        <header className={styles.reportHead}>
          <h2>{t('ZReport', { ns: 'cashDrawer' }).toUpperCase()}</h2>
          <p>{t('EndOfDaySummary', { ns: 'cashDrawer' })}</p>
          <p>
            {t('Session', { ns: 'cashDrawer' })} #{report.sessionId}
          </p>
        </header>

        <section className={styles.section}>
          <h3 className={styles.sectionTitle}>{t('SessionInformation', { ns: 'cashDrawer' })}</h3>
          <dl>
            <Row label={t('Cashier', { ns: 'cashDrawer' })} value={<bdi>{report.cashierName}</bdi>} />
            <Row label={t('OpenedAt', { ns: 'cashDrawer' })} value={formatDateTime(report.openedAt)} />
            <Row label={t('ClosedAt', { ns: 'cashDrawer' })} value={formatDateTime(report.closedAt)} />
            <Row label={t('Duration', { ns: 'cashDrawer' })} value={`${hours} ${t('Hours', { ns: 'cashDrawer' })}`} />
          </dl>
        </section>

        <section className={styles.section}>
          <h3 className={styles.sectionTitle}>{t('SalesSummary', { ns: 'cashDrawer' })}</h3>
          <dl>
            <Row label={t('TotalTransactions', { ns: 'cashDrawer' })} value={formatNumber(report.totalTransactions, 0)} />
            <Row label={t('GrossSales', { ns: 'cashDrawer' })} value={money(report.totalSales)} />
            <Row label={t('TotalDiscounts', { ns: 'cashDrawer' })} value={money(-report.totalDiscounts)} />
            <Row label={t('Adjustments', { ns: 'cashDrawer' })} value={signedMoney(report.totalAdjustments)} />
            <Row label={t('NetSales', { ns: 'cashDrawer' })} value={money(report.netSales)} total />
          </dl>
        </section>

        <section className={styles.section}>
          <h3 className={styles.sectionTitle}>{t('PaymentMethods', { ns: 'cashDrawer' })}</h3>
          <dl>
            <Row label={t('Cash', { ns: 'cashDrawer' })} value={money(report.cashSales)} />
            <Row label={t('Card', { ns: 'cashDrawer' })} value={money(report.cardSales)} />
            <Row label={t('Other', { ns: 'cashDrawer' })} value={money(report.otherPayments)} />
          </dl>
        </section>

        <section className={styles.section}>
          <h3 className={styles.sectionTitle}>{t('ZReport.CashReconciliation', { ns: 'cashDrawer' })}</h3>
          <dl>
            <Row label={t('OpeningCash', { ns: 'cashDrawer' })} value={money(report.openingCash)} />
            <Row label={t('CashSales', { ns: 'cashDrawer' })} value={signedMoney(report.cashSales)} />
            <Row label={`${t('CashDrops', { ns: 'cashDrawer' })} (${report.cashDropCount})`} value={money(-report.totalCashDrops)} />
            <Row label={t('ExpectedCash', { ns: 'cashDrawer' })} value={money(report.expectedCash)} total />
            <Row label={t('ActualCash', { ns: 'cashDrawer' })} value={money(report.actualCash)} total />
          </dl>
          <div className={styles.result} data-kind={difference.kind} role="status">
            <div className={styles.resultBody}>
              <span className={styles.resultLabel}>
                {difference.kind === 'surplus'
                  ? t('Overage', { ns: 'cashDrawer' })
                  : difference.kind === 'shortage'
                    ? t('Shortage', { ns: 'cashDrawer' })
                    : t('ZReport.PerfectBalance', { ns: 'cashDrawer' })}
              </span>
              {difference.kind !== 'balanced' && <span className={`${styles.resultAmount} tabular-nums`}>{signedMoney(difference.amount)}</span>}
            </div>
          </div>
        </section>

        {report.cashDrops.length > 0 && (
          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>{t('CashDropsDetail', { ns: 'cashDrawer' })}</h3>
            <table className={styles.reportTable}>
              <thead>
                <tr>
                  <th>{t('Time', { ns: 'cashDrawer' })}</th>
                  <th>{t('Amount', { ns: 'cashDrawer' })}</th>
                  <th>{t('Reason', { ns: 'cashDrawer' })}</th>
                  <th>{t('Notes', { ns: 'cashDrawer' })}</th>
                </tr>
              </thead>
              <tbody>
                {report.cashDrops.map((drop) => (
                  <tr key={drop.id}>
                    <td>{formatDateTime(drop.droppedAt)}</td>
                    <td className="tabular-nums">{money(drop.amount)}</td>
                    <td>{dropReasonLabel(t, drop.reason)}</td>
                    <td>{drop.notes ? <bdi>{drop.notes}</bdi> : '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        )}

        {report.orders.length > 0 && (
          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>{t('TransactionDetails', { ns: 'cashDrawer' })}</h3>
            <table className={styles.reportTable}>
              <thead>
                <tr>
                  <th>{t('Order', { ns: 'cashDrawer' })}</th>
                  <th>{t('Time', { ns: 'cashDrawer' })}</th>
                  <th>{t('Customer', { ns: 'cashDrawer' })}</th>
                  <th>{t('Payment', { ns: 'cashDrawer' })}</th>
                  <th className={styles.end}>{t('Amount', { ns: 'cashDrawer' })}</th>
                </tr>
              </thead>
              <tbody>
                {report.orders.map((order) => (
                  <tr key={order.orderId}>
                    <td>
                      <bdi>{order.orderNumber}</bdi>
                    </td>
                    <td>{formatDateTime(order.orderDate)}</td>
                    <td>{order.customerName ? <bdi>{order.customerName}</bdi> : t('WalkInCustomer', { ns: 'common' })}</td>
                    {/* the shared report always says "Cash" until payment methods are tracked per drawer */}
                    <td>{order.paymentMethod === 'Cash' ? t('Cash', { ns: 'cashDrawer' }) : order.paymentMethod}</td>
                    <td className={`${styles.end} tabular-nums`}>{money(order.totalAmount)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        )}

        <footer className={styles.reportFoot}>
          <p>
            <strong>{t('Generated', { ns: 'cashDrawer' })}:</strong> {formatDateTime(query.dataUpdatedAt)}
          </p>
          <p>{t('OfficialCashDrawerReconciliationReport', { ns: 'cashDrawer' })}</p>
        </footer>
      </article>
    </div>
  )
}
