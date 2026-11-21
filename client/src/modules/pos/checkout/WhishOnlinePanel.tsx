import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { money } from '../cart/cartUi'
import { cancelWhishRequest, createWhishRequest, useWhishRequest } from './api'
import styles from './checkout.module.css'

interface Props {
  walletId: number
  amount: number
  currencyId: number
  notation: string
  disabled: boolean
  /** Whish confirmed the payment: complete the sale with this request. */
  onPaid: (requestId: number) => void
  /** A request is out (waiting or paid): the dialog locks the amount and the method so the sale still matches it. */
  onActiveChange: (active: boolean) => void
}

function newKey(): string {
  return globalThis.crypto?.randomUUID?.() ?? `w${Date.now().toString(36)}${Math.random().toString(36).slice(2, 12)}`
}

/**
 * "Send payment request" for an online Whish payment. The customer scans the code (or opens the link) and pays in Whish; the panel asks
 * the till every few seconds and, once Whish says paid, hands the request to the dialog, which completes the sale.
 */
export function WhishOnlinePanel({ walletId, amount, currencyId, notation, disabled, onPaid, onActiveChange }: Props) {
  const { t } = useTranslation('pos')
  const [requestId, setRequestId] = useState<number | null>(null)
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // If the picture cannot load, the link below still works.
  const [qrFailed, setQrFailed] = useState(false)
  const query = useWhishRequest(requestId)
  const request = query.data
  const status = request?.status
  const handedOver = useRef<number | null>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const hasLink = Boolean(request?.collectUrl)

  // The code is what the customer needs: once it is there, scroll the dialog so all of it shows.
  useEffect(() => {
    if (hasLink) panelRef.current?.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' })
  }, [hasLink, requestId])

  const active = requestId !== null && (status === undefined || status === 'Pending' || status === 'Paid')
  useEffect(() => onActiveChange(active), [active, onActiveChange])

  useEffect(() => {
    if (request && status === 'Paid' && request.orderId == null && handedOver.current !== request.id) {
      handedOver.current = request.id
      onPaid(request.id)
    }
  }, [request, status, onPaid])

  const send = async () => {
    setStarting(true)
    setError(null)
    setQrFailed(false)
    try {
      const created = await createWhishRequest({ walletId, amount, currencyId, idempotencyKey: newKey() })
      setRequestId(created.id)
    } catch (e) {
      setError(isApiError(e) ? e.message : t('whishSendFailed'))
    } finally {
      setStarting(false)
    }
  }

  const stop = async () => {
    const id = requestId
    setRequestId(null)
    if (id !== null) await cancelWhishRequest(id).catch(() => undefined)
  }

  if (requestId === null || status === 'Cancelled') {
    return (
      <div className={styles.whishPanel}>
        <Button variant="primary" onClick={() => void send()} loading={starting} disabled={disabled || !(amount > 0)}>
          {t('whishSendRequest')}
        </Button>
        <p className={styles.hint}>{t('whishSendHint')}</p>
        {error && (
          <p className={styles.fieldError} role="alert">
            {error}
          </p>
        )}
      </div>
    )
  }

  if (status === 'Failed') {
    return (
      <div className={styles.whishPanel}>
        <div role="alert" className={styles.notice} data-tone="error">
          {request?.failureMessage ?? t('whishFailed')}
        </div>
        <Button onClick={() => void send()} loading={starting} disabled={disabled}>
          {t('tryAgain')}
        </Button>
      </div>
    )
  }

  const url = request?.collectUrl ?? null
  return (
    <div className={styles.whishPanel} aria-live="polite" ref={panelRef}>
      {/* The status first: the cashier must see it without scrolling past the code. */}
      {status === 'Paid' ? (
        <div className={styles.notice} data-tone="info">
          {request?.payerPhone ? t('whishPaidBy', { phone: request.payerPhone }) : t('whishPaid')}
        </div>
      ) : (
        <div className={styles.whishWaiting}>
          <Loader size="sm" tone="muted" />
          <span>{t('whishWaiting', { amount: money(amount, notation) })}</span>
          <Button variant="ghost" onClick={() => void stop()} disabled={disabled}>
            {t('whishStopWaiting')}
          </Button>
        </div>
      )}
      {url && status === 'Pending' && !qrFailed && (
        <img
          className={styles.whishQr}
          src={`/api/v1/pos/whish/requests/${requestId}/qr.svg`}
          alt={t('whishQrAlt')}
          width={220}
          height={220}
          onError={() => setQrFailed(true)}
        />
      )}
      {url && (
        <p className={styles.whishLink}>
          <span>{qrFailed ? t('whishOpenLink') : t('whishOrOpenLink')}</span>{' '}
          <a href={url} target="_blank" rel="noreferrer noopener" dir="ltr">
            {url}
          </a>
        </p>
      )}
    </div>
  )
}
