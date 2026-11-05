import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useParams } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { NotFoundPage } from '@/shell/pages/pages'
import { Button } from '@/ui/Button'
import { DataTable, type Column } from '@/ui/DataTable'
import { formatDate, formatDateTime, formatMoney, formatNumber } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { PageHeader } from '@/ui/PageHeader'
import { StatusBadge } from '@/ui/StatusBadge'
import { useReceiptPrinting } from '../_shared/receiptActions'
import { CancelPurchaseDialog } from './CancelPurchaseDialog'
import { usePurchase, type PurchaseItem } from './api'
import styles from './purchases.module.css'

/** `/app/purchases/:id` - the purchase note as it was saved: lines, totals, who entered it, and the receipt to print or download. */
export default function PurchaseDetailsPage() {
  const { t } = useTranslation(['purchases', 'common'])
  const id = Number(useParams().id)
  const canCancel = useCan(Permissions.Purchases_Cancel)
  const query = usePurchase(id)
  const printing = useReceiptPrinting()
  const [cancelling, setCancelling] = useState(false)

  if (!Number.isInteger(id) || id < 1 || (isApiError(query.error) && query.error.status === 404)) return <NotFoundPage />
  if (query.isPending) return <Loader fullSection label={t('Loading', { ns: 'common' })} />
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

  const p = query.data
  const cancelled = p.status === 'cancelled'
  const money = (amount: number) => formatMoney(amount, p.currencyNotation)

  const columns: Column<PurchaseItem>[] = [
    {
      key: 'product',
      header: t('Product'),
      required: true,
      cell: (i) => (
        <div className={styles.lineProduct}>
          <strong>
            <bdi>{i.productName}</bdi>
          </strong>
          {i.barcode && (
            <span className={styles.muted}>
              <bdi>{i.barcode}</bdi>
            </span>
          )}
        </div>
      ),
    },
    {
      key: 'quantity',
      header: t('Quantity'),
      align: 'end',
      cell: (i) => (
        <span className="tabular-nums">
          {formatNumber(i.quantity, 3)}
          {i.unit && i.unit !== 'piece' ? ` ${i.unit}` : ''}
        </span>
      ),
    },
    { key: 'unitCost', header: t('UnitCost'), align: 'end', cell: (i) => <span className="tabular-nums">{money(i.unitCost)}</span> },
    {
      key: 'costUpdated',
      header: t('CostPriceUpdated'),
      align: 'center',
      hideBelow: 800,
      cell: (i) => (i.updatedCostPrice ? <StatusBadge tone="info" label={t('Yes')} /> : <span className={styles.muted}>-</span>),
    },
    { key: 'lineTotal', header: t('LineTotal'), align: 'end', cell: (i) => <strong className="tabular-nums">{money(i.lineTotal)}</strong> },
  ]

  return (
    <div className={styles.page}>
      <PageHeader title={p.number} back={{ to: '/purchases', label: t('BackToPurchases') }}>
        <StatusBadge filled tone={cancelled ? 'danger' : 'success'} label={cancelled ? t('StatusCancelled') : t('StatusReceived')} />
        <Button loading={printing.busy} onClick={() => void printing.print(`/purchases/${p.id}/print`, `/purchases/${p.id}/pdf`, `${p.number}.pdf`)}>
          {t('PrintReceipt')}
        </Button>
        <Button onClick={() => void printing.downloadPdf(`/purchases/${p.id}/pdf`, `${p.number}.pdf`)}>{t('DownloadPdf')}</Button>
        {canCancel && !cancelled && (
          <Button variant="danger" onClick={() => setCancelling(true)}>
            {t('CancelPurchase')}
          </Button>
        )}
      </PageHeader>

      {cancelled && (
        <div className={styles.cancelBanner} role="status">
          <strong>{t('CancelledBanner', { date: p.cancelledAt ? formatDateTime(p.cancelledAt) : '', by: p.cancelledBy ?? '-' })}</strong>
          {p.cancelReason && <span>{p.cancelReason}</span>}
        </div>
      )}

      <div className={styles.details}>
        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('PurchaseInformation')}</h2>
            <dl className={styles.facts}>
              <div>
                <dt>{t('Supplier')}</dt>
                <dd>
                  <Link className={styles.link} to={`/suppliers/${p.supplierId}`}>
                    <bdi>{p.supplierName}</bdi>
                  </Link>
                </dd>
              </div>
              <div>
                <dt>{t('PurchaseDate')}</dt>
                <dd>{formatDate(p.purchaseDate)}</dd>
              </div>
              <div>
                <dt>{t('SupplierReference')}</dt>
                <dd>{p.supplierReference ? <bdi>{p.supplierReference}</bdi> : <span className={styles.muted}>-</span>}</dd>
              </div>
              <div>
                <dt>{t('EnteredBy')}</dt>
                <dd>{p.createdBy ? <bdi>{p.createdBy}</bdi> : <span className={styles.muted}>-</span>}</dd>
              </div>
              {p.notes && (
                <div className={styles.factWide}>
                  <dt>{t('Notes')}</dt>
                  <dd>
                    <bdi>{p.notes}</bdi>
                  </dd>
                </div>
              )}
            </dl>
          </section>

          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('Products')}</h2>
            <DataTable<PurchaseItem> caption={t('Products')} columns={columns} rows={[...p.items]} rowKey={(i) => i.id} />
          </section>
        </div>

        <div className={styles.column}>
          <section className={styles.card}>
            <h2 className={styles.cardTitle}>{t('PurchaseSummary')}</h2>
            <dl className={styles.summary}>
              <div className={styles.summaryRow}>
                <dt>{t('Subtotal')}</dt>
                <dd className="tabular-nums">{money(p.subtotal)}</dd>
              </div>
              {p.discountAmount > 0 && (
                <div className={styles.summaryRow}>
                  <dt>{t('DiscountWithPercent', { percent: formatNumber(p.discountPercent, 2) })}</dt>
                  <dd className="tabular-nums">-{money(p.discountAmount)}</dd>
                </div>
              )}
              <div className={styles.summaryRow}>
                <dt>{t('Total')}</dt>
                <dd className={`${styles.grand} tabular-nums`}>{money(p.total)}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{p.paidFromDrawer ? t('PaidFromDrawer') : t('Paid')}</dt>
                <dd className="tabular-nums">{money(p.amountPaid)}</dd>
              </div>
              <div className={styles.summaryRow}>
                <dt>{t('LeftToPay')}</dt>
                <dd className={styles.money} data-tone={!cancelled && p.amountDue > 0 ? 'owe' : undefined}>
                  {money(p.amountDue)}
                </dd>
              </div>
            </dl>
            <p className={styles.hint}>{cancelled ? t('CancelledNote') : t('LedgerNote')}</p>
          </section>
        </div>
      </div>

      {cancelling && <CancelPurchaseDialog purchase={p} onClose={() => setCancelling(false)} />}
    </div>
  )
}
