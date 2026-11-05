import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { Permissions } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { Can } from '@/core/permissions/permissions'
import { BackLink } from '@/ui/BackLink'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { StatusBadge } from '@/ui/StatusBadge'
import { formatDateTime, formatMoney, formatNumber, formatPercent, formatQty } from '@/ui/format'
import { ChangeCustomerDialog } from '../_shared/ChangeCustomerDialog'
import { useReceiptPrinting } from '../_shared/receiptActions'
import sales from '../_shared/sales.module.css'
import { useReceipt, type ReceiptDetail } from './api'
import { VoidReceiptDialog } from './VoidReceiptDialog'

/** `/receipts/:id`: one receipt with its lines, totals, payments and history; reprint, PDF and void. */
export default function ReceiptDetailsPage() {
  const params = useParams()
  const id = Number(params.id)
  const validId = Number.isInteger(id) && id > 0
  const receipt = useReceipt(validId ? id : null)

  if (!validId || receipt.error) return <NotFound error={receipt.error} />
  if (receipt.isPending) return <Loader fullSection size="lg" />
  return <Details receipt={receipt.data} />
}

function NotFound({ error }: { error?: unknown }) {
  const { t } = useTranslation('receipts')
  const navigate = useNavigate()
  const message = isApiError(error) && error.status !== 404 ? error.message : t('details.notFound')
  return (
    <div className={`${sales.page} ${sales.narrow}`}>
      <div className={sales.card} role="alert">
        <h1 className={sales.title}>{message}</h1>
        <div className={sales.buttons}>
          <Button variant="primary" onClick={() => navigate('/receipts')}>
            {t('BackToReceipts')}
          </Button>
        </div>
      </div>
    </div>
  )
}

function Details({ receipt: r }: { receipt: ReceiptDetail }) {
  const { t } = useTranslation('receipts')
  const printing = useReceiptPrinting()
  const { t: tc } = useTranslation('common')
  const [voiding, setVoiding] = useState(false)
  const [changingCustomer, setChangingCustomer] = useState(false)
  const money = (amount: number) => formatMoney(amount, r.currencyNotation)
  const statusBadge = r.isVoid ? (
    <StatusBadge tone="danger" label={t('Void')} />
  ) : r.isFullyPaid ? (
    <StatusBadge tone="success" label={t('Paid')} />
  ) : (
    <StatusBadge tone="warning" label={t('Partial')} />
  )

  return (
    <div className={`${sales.page} ${sales.narrow}`}>
      <div className={sales.top}>
        <div className={sales.heading}>
          <BackLink to="/receipts">{t('BackToReceipts')}</BackLink>
          <h1 className={sales.title}>
            <bdi>{r.receiptNumber}</bdi>
          </h1>
          <p className={sales.subtitle}>{t('ReceiptDetails')}</p>
        </div>
        <div className={sales.buttons}>
          <Button variant="primary" loading={printing.busy} onClick={() => void printing.print(`/receipts/${r.id}/print`, `/receipts/${r.id}/pdf`, `${r.receiptNumber}.pdf`)}>
            {t('PrintReceipt')}
          </Button>
          <Button onClick={() => void printing.downloadPdf(`/receipts/${r.id}/pdf`, `${r.receiptNumber}.pdf`)}>{t('DownloadPDF')}</Button>
          {!r.isVoid && (
            <Can permission={Permissions.Sales_ChangeCustomer}>
              <Button onClick={() => setChangingCustomer(true)}>{tc('SaleCustomer_Change')}</Button>
              <Button variant="danger" onClick={() => setVoiding(true)}>
                {t('VoidReceipt')}
              </Button>
            </Can>
          )}
        </div>
      </div>

      {r.isVoid && (
        <div className={sales.notice} data-tone="danger" role="status">
          <strong>{t('ThisReceiptHasBeenVoided')}</strong>
          {r.voidReason && (
            <div>
              {t('Reason')}: {r.voidReason}
            </div>
          )}
          {r.voidDate && (
            <div>
              {t('Voided')}: {formatDateTime(r.voidDate)}
              {r.voidedBy ? ` · ${r.voidedBy}` : ''}
            </div>
          )}
        </div>
      )}

      <div className={sales.layout}>
        <div className={sales.side}>
          <section className={sales.card} aria-label={t('ReceiptInformation')}>
            <h2 className={sales.cardTitle}>{t('ReceiptInformation')}</h2>
            <dl className={sales.grid}>
              <Item label={t('Receipt')}>
                <strong dir="ltr">{r.receiptNumber}</strong>
              </Item>
              <Item label={t('Date')}>{formatDateTime(r.receiptDate)}</Item>
              <Item label={t('details.order')}>
                <Link className={sales.link} to={`/orders/${r.orderId}`}>
                  <bdi>ORD-{String(r.orderId).padStart(6, '0')}</bdi>
                </Link>
              </Item>
              <Item label={t('Index.Customer')}>{r.customerName ?? <span className={sales.muted}>{t('WalkInCustomer')}</span>}</Item>
              {r.customerPhone && (
                <Item label={t('details.phone')}>
                  <span dir="ltr">{r.customerPhone}</span>
                </Item>
              )}
              <Item label={t('Index.Cashier')}>{r.cashierName || '–'}</Item>
            </dl>
          </section>

          <section className={sales.card} aria-label={t('Item')}>
            <h2 className={sales.cardTitle}>{t('details.items')}</h2>
            <div className={sales.scroll}>
              <table className={sales.lines}>
                <thead>
                  <tr>
                    <th scope="col">{t('Item')}</th>
                    <th scope="col" data-align="center">
                      {t('Quantity')}
                    </th>
                    <th scope="col" data-align="end">
                      {t('UnitPrice')}
                    </th>
                    <th scope="col" data-align="end">
                      {t('Discount')}
                    </th>
                    <th scope="col" data-align="end">
                      {t('Tax')}
                    </th>
                    <th scope="col" data-align="end">
                      {t('Total')}
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {r.lines.map((line, index) => (
                    <tr key={`${line.productName}-${index}`}>
                      <td>
                        <strong>{line.productName}</strong>
                        {line.barcode && (
                          <span className={sales.sku} dir="ltr">
                            {line.barcode}
                          </span>
                        )}
                      </td>
                      <td data-align="center" className="tabular-nums">
                        {formatQty(line.quantity)}
                        {line.unit && line.unit !== 'piece' ? ` ${line.unit}` : ''}
                      </td>
                      <td data-align="end" className="tabular-nums">
                        {money(line.unitPrice)}
                      </td>
                      <td data-align="end" className="tabular-nums">
                        {line.discountAmount > 0 ? formatMoney(-line.discountAmount, r.currencyNotation) : '–'}
                      </td>
                      <td data-align="end" className="tabular-nums">
                        {money(line.taxAmount)}
                      </td>
                      <td data-align="end" className="tabular-nums">
                        <strong>{money(line.lineTotal)}</strong>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            <dl className={sales.totals}>
              <div>
                <dt>{t('details.subtotal')}</dt>
                <dd className="tabular-nums">{money(r.subTotal)}</dd>
              </div>
              {r.discountAmount > 0 && (
                <div>
                  <dt>{t('details.discountWithPercent', { 0: formatPercent(r.discountPercentage, 2) })}</dt>
                  <dd className="tabular-nums" data-tone="danger">
                    {formatMoney(-r.discountAmount, r.currencyNotation)}
                  </dd>
                </div>
              )}
              <div>
                <dt>{t('details.tax')}</dt>
                <dd className="tabular-nums">{money(r.taxAmount)}</dd>
              </div>
              {r.adjustment !== 0 && (
                <div>
                  <dt>{r.adjustment > 0 ? t('details.surcharge') : t('details.adjustment')}</dt>
                  <dd className="tabular-nums" data-tone={r.adjustment > 0 ? 'danger' : 'success'}>
                    {r.adjustment > 0 ? '+' : ''}
                    {money(r.adjustment)}
                  </dd>
                </div>
              )}
              <div className={sales.grand}>
                <dt>{t('details.totalAmount')}</dt>
                <dd className="tabular-nums">{money(r.totalAmount)}</dd>
              </div>
              <div>
                <dt>{t('details.amountPaid')}</dt>
                <dd className="tabular-nums" data-tone="success">
                  {money(r.amountPaid)}
                </dd>
              </div>
              {r.changeAmount > 0 && (
                <div>
                  <dt>{t('details.change')}</dt>
                  <dd className="tabular-nums">{money(r.changeAmount)}</dd>
                </div>
              )}
            </dl>
          </section>

          {r.payments.length > 0 && (
            <section className={sales.card} aria-label={t('PaymentDetails')}>
              <h2 className={sales.cardTitle}>{t('PaymentDetails')}</h2>
              <div className={sales.scroll}>
                <table className={sales.lines}>
                  <thead>
                    <tr>
                      <th scope="col">{t('PaymentMethod')}</th>
                      <th scope="col">{t('ReferenceNumber')}</th>
                      <th scope="col" data-align="end">
                        {t('Amount')}
                      </th>
                      <th scope="col">{t('Date')}</th>
                    </tr>
                  </thead>
                  <tbody>
                    {r.payments.map((p, index) => (
                      <tr key={index}>
                        <td>{p.paymentMethod}</td>
                        <td>{p.referenceNumber ?? '–'}</td>
                        <td data-align="end" className="tabular-nums">
                          <strong>{money(p.amount)}</strong>
                        </td>
                        <td>{formatDateTime(p.paymentDate)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </div>

        <div className={sales.side}>
          <section className={sales.card} aria-label={t('PaymentSummary')}>
            <h2 className={sales.cardTitle}>{t('PaymentSummary')}</h2>
            <dl className={sales.grid}>
              <Item label={t('PaymentMethod')}>{r.paymentMethod || '–'}</Item>
              <Item label={t('details.currency')}><bdi>{r.currencyCode ? `${r.currencyCode} (${r.currencyNotation})` : r.currencyNotation}</bdi></Item>
              {r.exchangeRate !== 1 && <Item label={t('details.exchangeRate')}>{formatNumber(r.exchangeRate, 4)}</Item>}
              <Item label={t('Index.Status')}>{statusBadge}</Item>
            </dl>
          </section>

          <section className={sales.card} aria-label={t('ReceiptHistory')}>
            <h2 className={sales.cardTitle}>{t('ReceiptHistory')}</h2>
            <ol className={sales.timeline}>
              <li>
                <strong>{t('ReceiptCreated')}</strong>
                <small>{formatDateTime(r.createdAt)}</small>
              </li>
              {r.printedAt && (
                <li>
                  <strong>{t('Printed')}</strong>
                  <small>
                    {formatDateTime(r.printedAt)}
                    {r.printedBy ? ` · ${r.printedBy}` : ''}
                  </small>
                </li>
              )}
              {r.isVoid && r.voidDate && (
                <li>
                  <strong>{t('Voided')}</strong>
                  <small>
                    {formatDateTime(r.voidDate)}
                    {r.voidedBy ? ` · ${r.voidedBy}` : ''}
                  </small>
                </li>
              )}
            </ol>
          </section>

          {r.notes && (
            <section className={sales.card} aria-label={t('Notes')}>
              <h2 className={sales.cardTitle}>{t('Notes')}</h2>
              <p className={sales.itemValue}>{r.notes}</p>
            </section>
          )}
        </div>
      </div>

      {changingCustomer && (
        <ChangeCustomerDialog
          kind="receipts"
          id={r.id}
          number={r.receiptNumber}
          customerId={r.customerId ?? null}
          customerName={r.customerName ?? null}
          onClose={() => setChangingCustomer(false)}
        />
      )}
      {voiding && <VoidReceiptDialog receipt={{ id: r.id, receiptNumber: r.receiptNumber }} onClose={() => setVoiding(false)} />}
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
