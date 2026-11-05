import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useSession } from '@/core/auth/useSession'
import { Can } from '@/core/permissions/permissions'
import { flattenNav } from '@/shell/nav'
import { BackLink } from '@/ui/BackLink'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { StatusBadge } from '@/ui/StatusBadge'
import { formatDateTime, formatMoney, formatPercent, formatQty } from '@/ui/format'
import { ChangeCustomerDialog } from '../_shared/ChangeCustomerDialog'
import { useReceiptPrinting } from '../_shared/receiptActions'
import sales from '../_shared/sales.module.css'
import { STATUS_KEYS, STATUS_TONES, useOrder, type OrderDetail } from './api'
import { OrderStatusDialog } from './OrderStatusDialog'

/** `/orders/:id`: the order's lines, totals, receipts and timeline, with status change and reprint. */
export default function OrderDetailsPage() {
  const params = useParams()
  const id = Number(params.id)
  const validId = Number.isInteger(id) && id > 0
  const order = useOrder(validId ? id : null)

  if (!validId || order.error) return <NotFound error={order.error} />
  if (order.isPending) return <Loader fullSection size="lg" />
  return <Details order={order.data} />
}

function NotFound({ error }: { error?: unknown }) {
  const { t } = useTranslation('orders')
  const navigate = useNavigate()
  const message = isApiError(error) && error.status !== 404 ? error.message : t('details.notFound')
  return (
    <div className={`${sales.page} ${sales.narrow}`}>
      <div className={sales.card} role="alert">
        <h1 className={sales.title}>{message}</h1>
        <div className={sales.buttons}>
          <Button variant="primary" onClick={() => navigate('/orders')}>
            {t('BackToOrders')}
          </Button>
        </div>
      </div>
    </div>
  )
}

function Details({ order: o }: { order: OrderDetail }) {
  const { t } = useTranslation('orders')
  const session = useSession()
  const printing = useReceiptPrinting()
  const { t: tc } = useTranslation('common')
  const [changing, setChanging] = useState(false)
  const [changingCustomer, setChangingCustomer] = useState(false)
  const money = (amount: number) => formatMoney(amount, o.currencyNotation)
  const customers = flattenNav(session.nav).find((item) => item.key === 'Customers')
  // The customers page is still the Razor one until S3 lands; follow whichever the session says is live.
  const customerHref = customers && o.customerId ? (customers.migrated ? `${customers.href}/${o.customerId}` : `${customers.href}/Details/${o.customerId}`) : null

  return (
    <div className={`${sales.page} ${sales.narrow}`}>
      <div className={sales.top}>
        <div className={sales.heading}>
          <BackLink to="/orders">{t('BackToOrders')}</BackLink>
          <h1 className={sales.title}>
            <bdi>{o.orderNumber}</bdi>
          </h1>
          <p className={sales.subtitle}>{t('OrderDetails')}</p>
        </div>
        <div className={sales.buttons}>
          {/* A cancelled order is final: it cannot be cancelled again or reopened. */}
          {o.status !== 4 && (
            <Can permission={Permissions.Orders_Edit}>
              <Button onClick={() => setChanging(true)}>{t('UpdateStatus')}</Button>
            </Can>
          )}
          {/* The customer of a cancelled or returned sale is history: the server refuses it, so the button is not offered. */}
          {o.status !== 4 && o.status !== 5 && (
            <Can permission={Permissions.Sales_ChangeCustomer}>
              <Button onClick={() => setChangingCustomer(true)}>{tc('SaleCustomer_Change')}</Button>
            </Can>
          )}
          <Button
            variant="primary"
            loading={printing.busy}
            onClick={() => void printing.print(`/pos/sales/${o.id}/print`, `/pos/sales/${o.id}/receipt.pdf`, `${o.orderNumber}.pdf`)}
          >
            {t('PrintReceipt')}
          </Button>
          <Button onClick={() => void printing.downloadPdf(`/pos/sales/${o.id}/receipt.pdf`, `${o.orderNumber}.pdf`)}>{t('details.downloadPdf')}</Button>
        </div>
      </div>

      <div className={sales.layout}>
        <div className={sales.side}>
          <section className={sales.card} aria-label={t('OrderInformation')}>
            <div className={sales.cardHead}>
              <h2 className={sales.cardTitle}>{t('OrderInformation')}</h2>
              <StatusBadge tone={STATUS_TONES[o.status] ?? 'neutral'} label={t(STATUS_KEYS[o.status] ?? 'Pending')} />
            </div>
            <dl className={sales.grid}>
              <Item label={t('OrderDate')}>{formatDateTime(o.orderDate)}</Item>
              <Item label={t('Customer')}>
                {o.customerName ? (
                  customerHref ? (
                    <a className={sales.link} href={customerHref}>
                      {o.customerName}
                    </a>
                  ) : (
                    o.customerName
                  )
                ) : (
                  <span className={sales.muted}>{t('list.walkIn')}</span>
                )}
              </Item>
              <Item label={t('TotalAmount')}>
                <strong className="tabular-nums">{money(o.totalAmount)}</strong>
              </Item>
              <Item label={t('details.currency')}><bdi>{o.currencyCode ? `${o.currencyCode} (${o.currencyNotation})` : o.currencyNotation}</bdi></Item>
            </dl>
          </section>

          <section className={sales.card} aria-label={t('OrderItems')}>
            <h2 className={sales.cardTitle}>{t('OrderItems')}</h2>
            {o.lines.length === 0 ? (
              <p className={sales.help}>{t('NoItemsFoundInThisOrder')}</p>
            ) : (
              <div className={sales.scroll}>
                <table className={sales.lines}>
                  <thead>
                    <tr>
                      <th scope="col">{t('Product')}</th>
                      <th scope="col" data-align="center">
                        {t('Qty')}
                      </th>
                      <th scope="col" data-align="end">
                        {t('UnitPrice')}
                      </th>
                      <th scope="col" data-align="end">
                        {t('Total')}
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {o.lines.map((line) => (
                      <tr key={line.productId}>
                        <td>
                          <strong>{line.productName || t('details.unknownProduct')}</strong>
                          {line.barcode && (
                            <span className={sales.sku} dir="ltr">
                              {line.barcode}
                            </span>
                          )}
                        </td>
                        <td data-align="center" className="tabular-nums">
                          {line.weightOrVolume ? formatQty(line.weightOrVolume) : formatQty(line.quantity)}
                        </td>
                        <td data-align="end" className="tabular-nums">
                          {money(line.unitPrice)}
                        </td>
                        <td data-align="end" className="tabular-nums">
                          <strong>{money(line.totalPrice)}</strong>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>
        </div>

        <div className={sales.side}>
          <section className={sales.card} aria-label={t('OrderSummary')}>
            <h2 className={sales.cardTitle}>{t('OrderSummary')}</h2>
            <dl className={sales.totals}>
              <div>
                <dt>{t('Subtotal')}</dt>
                <dd className="tabular-nums">{money(o.subtotal)}</dd>
              </div>
              {o.discountAmount > 0 && (
                <div data-tone="success">
                  <dt>{t('DiscountWithPercent', { 0: formatPercent(o.discountPercentage, 2) })}</dt>
                  <dd className="tabular-nums" data-tone="success">
                    {formatMoney(-o.discountAmount, o.currencyNotation)}
                  </dd>
                </div>
              )}
              {o.adjustment !== 0 && (
                <div>
                  <dt>{o.adjustment > 0 ? t('Surcharge') : t('Adjustment')}</dt>
                  <dd className="tabular-nums" data-tone={o.adjustment > 0 ? 'danger' : 'success'}>
                    {o.adjustment > 0 ? '+' : ''}
                    {money(o.adjustment)}
                  </dd>
                </div>
              )}
              <div>
                <dt>{t('Tax')}</dt>
                <dd className="tabular-nums">{money(o.taxAmount)}</dd>
              </div>
              <div className={sales.grand}>
                <dt>{t('Total')}</dt>
                <dd className="tabular-nums">{money(o.totalAmount)}</dd>
              </div>
            </dl>
          </section>

          <section className={sales.card} aria-label={t('details.receipts')}>
            <h2 className={sales.cardTitle}>{t('details.receipts')}</h2>
            {o.receipts.length === 0 ? (
              <p className={sales.help}>{t('details.noReceipts')}</p>
            ) : (
              <ul className={sales.refs}>
                {o.receipts.map((r) => (
                  <li key={r.id}>
                    <Link className={sales.link} to={`/receipts/${r.id}`}>
                      <bdi>{r.receiptNumber}</bdi>
                    </Link>
                    {r.isVoid && <StatusBadge tone="danger" label={t('details.void')} />}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className={sales.card} aria-label={t('OrderTimeline')}>
            <h2 className={sales.cardTitle}>{t('OrderTimeline')}</h2>
            <ol className={sales.timeline}>
              <li>
                <strong>{t('OrderPlaced')}</strong>
                <small>{formatDateTime(o.createdAt)}</small>
              </li>
              {o.updatedAt && (
                <li>
                  <strong>{t('StatusUpdated')}</strong>
                  <small>
                    {t(STATUS_KEYS[o.status] ?? 'Pending')} · {formatDateTime(o.updatedAt)}
                  </small>
                </li>
              )}
            </ol>
          </section>
        </div>
      </div>

      {changingCustomer && (
        <ChangeCustomerDialog
          kind="orders"
          id={o.id}
          number={o.orderNumber}
          customerId={o.customerId ?? null}
          customerName={o.customerName ?? null}
          onClose={() => setChangingCustomer(false)}
        />
      )}
      {changing && <OrderStatusDialog order={{ id: o.id, orderNumber: o.orderNumber, status: o.status }} onClose={() => setChanging(false)} />}
    </div>
  )
}

function Item({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className={sales.item}>
      <dt className={sales.itemLabel}>{label}</dt>
      <dd className={sales.itemValue}>{children}</dd>
    </div>
  )
}
