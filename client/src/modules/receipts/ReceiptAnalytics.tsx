import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { ChipGroup } from '@/ui/ChipGroup'
import { formatDate, formatMoney, formatNumber } from '@/ui/format'
import sales from '../_shared/sales.module.css'
import type { ReceiptStats } from './api'

/**
 * The old Analytics tab (payment-method doughnut, daily-sales line) without a chart library: ranked bars for the payment methods and one
 * column per day for the sales. Money is never mixed across currencies, so with more than one a chip switches the daily chart.
 */
export function ReceiptAnalytics({ stats }: { stats: ReceiptStats }) {
  const { t } = useTranslation('receipts')
  const [picked, setPicked] = useState<string | null>(null)
  const currency = stats.currencies.find((c) => String(c.currencyId) === picked) ?? stats.currencies[0]
  const maxDay = Math.max(0, ...(currency?.daily.map((d) => d.amount) ?? []))
  const maxCount = Math.max(0, ...stats.paymentMethods.map((p) => p.count))

  return (
    <div className={sales.analytics}>
      <section className={sales.card} aria-label={t('PaymentMethods')}>
        <h2 className={sales.cardTitle}>{t('PaymentMethods')}</h2>
        {stats.paymentMethods.length === 0 ? (
          <p className={sales.help}>{t('details.noData')}</p>
        ) : (
          <ul className={sales.bars}>
            {stats.paymentMethods.map((p) => (
              <li key={p.paymentMethod} className={sales.bar}>
                <span>{p.paymentMethod}</span>
                <span aria-hidden="true">
                  <i style={{ inlineSize: `${maxCount ? (p.count / maxCount) * 100 : 0}%` }} />
                </span>
                <strong className="tabular-nums">{formatNumber(p.count, 0)}</strong>
              </li>
            ))}
          </ul>
        )}
      </section>

      <section className={sales.card} aria-label={t('DailySalesTrend')}>
        <div className={sales.cardHead}>
          <h2 className={sales.cardTitle}>{t('DailySalesTrend')}</h2>
          {stats.currencies.length > 1 && (
            <ChipGroup
              size="sm"
              aria-label={t('Currency')}
              options={stats.currencies.map((c) => ({ value: String(c.currencyId), label: c.currencyNotation }))}
              value={String(currency?.currencyId)}
              onChange={setPicked}
            />
          )}
        </div>
        {!currency || currency.daily.length === 0 ? (
          <p className={sales.help}>{t('details.noData')}</p>
        ) : (
          <>
            <div className={sales.days} role="img" aria-label={t('DailySalesTrend')}>
              {currency.daily.map((d) => (
                <div
                  key={d.date}
                  style={{ blockSize: `${maxDay ? Math.max(2, (d.amount / maxDay) * 100) : 2}%` }}
                  title={`${formatDate(d.date)}: ${formatMoney(d.amount, currency.currencyNotation)}`}
                />
              ))}
            </div>
            <p className={sales.help}>
              {formatDate(currency.daily[0]!.date)} – {formatDate(currency.daily[currency.daily.length - 1]!.date)} · {t('details.peakDay', { 0: formatMoney(maxDay, currency.currencyNotation) })}
            </p>
          </>
        )}
      </section>
    </div>
  )
}
