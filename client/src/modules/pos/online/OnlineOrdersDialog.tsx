import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { formatDateTime } from '@/ui/format'
import { toast } from '@/ui/toast'
import { addToCart } from '../cart/addToCart'
import { useCartStore } from '../store/cartStore'
import { Permissions } from '@/api/permissions'
import { useCan } from '@/core/permissions/permissions'
import { rememberCustomer } from '../checkout/customerNames'
import { useQuickCreateCustomer } from '../checkout/api'
import { fetchPosProduct, findCustomersByPhone, useMoveOnlineOrder, useOnlineOrders, type MatchedCustomer, type OnlineOrder, type OnlineOrderMove } from './api'
import styles from './online.module.css'

interface Props {
  onClose: () => void
}

interface Asking {
  order: OnlineOrder
  matches: MatchedCustomer[]
  choice: number | 'new' | 'walkin'
  firstName: string
  lastName: string
  error?: string
}

/**
 * Orders guests sent from the online menu to this till. "Load into sale" puts the dishes on the cart (the till's own products and
 * prices, through the normal add path, so weighed products still ask for the amount) and tells the guest it was accepted. The cashier
 * then takes payment as usual, and marks the order ready and done so the guest's phone follows.
 * An order with a phone asks who it is for first: a POS customer with that number is suggested (the guest typed the number and nobody
 * checked it, so the cashier confirms, and a debt is never charged without choosing that payment); with no match the cashier may save
 * them as a new customer (off by default: most table orders are one-off walk-ins).
 */
export function OnlineOrdersDialog({ onClose }: Props) {
  const { t } = useTranslation('pos')
  const orders = useOnlineOrders()
  const move = useMoveOnlineOrder()
  const [busy, setBusy] = useState<number | null>(null)
  const createCustomer = useQuickCreateCustomer()
  const canCreate = useCan(Permissions.Customers_Create)

  const step = async (order: OnlineOrder, status: OnlineOrderMove) => {
    setBusy(order.id)
    try {
      await move.mutateAsync({ id: order.id, status })
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('onlineFailed'))
    } finally {
      setBusy(null)
    }
  }

  const reject = async (order: OnlineOrder) => {
    const ok = await confirm({
      title: t('onlineReject'),
      message: t('onlineRejectQ', { number: order.number }),
      confirmLabel: t('onlineReject'),
      danger: true,
      onConfirm: async () => {
        await move.mutateAsync({ id: order.id, status: 'rejected' })
      },
    })
    if (ok) toast.success(t('onlineRejected', { number: order.number }))
  }

  /** Who an order with a phone is for, asked before loading it: the matching customer (ticked), a new one (unticked), or a walk-in. */
  const [asking, setAsking] = useState<Asking | null>(null)

  const load = async (order: OnlineOrder) => {
    if (useCartStore.getState().lines.length > 0) {
      const ok = await confirm({ title: t('onlineLoad'), message: t('onlineAddToSaleQ'), confirmLabel: t('onlineLoad') })
      if (!ok) return
    }
    if (!order.phone) return void put(order, null)
    setBusy(order.id)
    let matches: MatchedCustomer[] = []
    try {
      matches = await findCustomersByPhone(order.phone)
    } catch {
      // Cannot look the number up: carry on as a walk-in rather than block the order.
    } finally {
      setBusy(null)
    }
    const [first, ...rest] = order.name.trim().split(/\s+/)
    setAsking({ order, matches, choice: matches[0] ? matches[0].id : 'walkin', firstName: first ?? '', lastName: rest.join(' ') })
  }

  /** Puts the order on the sale, with the customer the cashier chose (null = walk-in: the cart's customer is left as it is). */
  const put = async (order: OnlineOrder, customer: { id: number; name: string } | null) => {
    setBusy(order.id)
    let added = 0
    const missing: string[] = []
    try {
      for (const line of order.lines) {
        if (line.posProductId === null) {
          missing.push(line.name)
          continue
        }
        try {
          const product = await fetchPosProduct(line.posProductId)
          if (await addToCart(product, { quantity: line.quantity })) added++
        } catch {
          missing.push(line.name)
        }
      }
      if (customer) {
        rememberCustomer(customer.id, customer.name)
        useCartStore.getState().setCustomerId(customer.id)
      }
      // The order's who and where go on the sale's note, so the receipt says whose it is.
      const who = [t('onlineNote', { number: order.number }), order.name, order.place, order.note].filter(Boolean).join(' · ')
      const { notes, setNotes } = useCartStore.getState()
      setNotes(notes ? `${notes}\n${who}` : who)
      if (order.status === 'new') await move.mutateAsync({ id: order.id, status: 'accepted' })
      toast.success(t('onlineLoaded', { number: order.number, count: added }))
      for (const name of missing) toast.warning(t('onlineNotInPos', { name }))
      onClose()
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('onlineFailed'))
    } finally {
      setBusy(null)
    }
  }

  /** "Load into sale" on the who-is-it step: attach the match, create the new customer first, or go on as a walk-in. */
  const confirmWho = async () => {
    if (!asking) return
    const { order, matches, choice, firstName, lastName } = asking
    if (choice === 'walkin') {
      setAsking(null)
      return void put(order, null)
    }
    if (choice === 'new') {
      if (firstName.trim().length < 2 || lastName.trim().length < 2) return setAsking({ ...asking, error: t('onlineNameNeeded') })
      setBusy(order.id)
      try {
        const created = await createCustomer.mutateAsync({ firstName: firstName.trim(), lastName: lastName.trim(), phoneNumber: order.phone ?? undefined })
        setAsking(null)
        return void put(order, { id: created.id, name: created.name })
      } catch (error) {
        setBusy(null)
        return setAsking({ ...asking, error: isApiError(error) ? error.message : t('onlineFailed') })
      }
    }
    const match = matches.find((m) => m.id === choice)
    setAsking(null)
    void put(order, match ? { id: match.id, name: match.name } : null)
  }

  if (asking) {
    const { order, matches, choice } = asking
    const set = (patch: Partial<Asking>) => setAsking({ ...asking, ...patch, error: undefined })
    return (
      <Modal onClose={() => setAsking(null)} width={520} title={t('onlineWho', { number: order.number })}>
        <ModalBody>
          <p className={styles.plain}>
            <strong dir="auto">{order.name}</strong> · <span dir="ltr">{order.phone}</span>
          </p>
          <fieldset className={styles.who2}>
            <legend className={styles.srOnly}>{t('onlineWho', { number: order.number })}</legend>
            {matches.map((m) => (
              <label key={m.id} className={styles.pick}>
                <input type="radio" name="online-who" checked={choice === m.id} onChange={() => set({ choice: m.id })} />
                <span>
                  <strong dir="auto">{m.name}</strong>
                  <span className={styles.hint} dir="ltr">
                    {m.phoneNumber}
                  </span>
                  {m.balance > 0 && <span className={styles.owes}>{t('onlineOwes', { amount: m.balance.toLocaleString('en-US') })}</span>}
                </span>
              </label>
            ))}
            {matches.length === 0 && <p className={styles.hint}>{t('onlineNoMatch')}</p>}
            {matches.length === 0 && canCreate && (
              <label className={styles.pick}>
                <input type="radio" name="online-who" checked={choice === 'new'} onChange={() => set({ choice: 'new' })} />
                <span>
                  <strong>{t('onlineNewCustomer')}</strong>
                  <span className={styles.hint}>{t('onlineNewCustomerHint')}</span>
                </span>
              </label>
            )}
            {choice === 'new' && (
              <div className={styles.names}>
                <label>
                  <span>{t('onlineFirstName')}</span>
                  <input value={asking.firstName} onChange={(e) => set({ firstName: e.target.value })} dir="auto" maxLength={100} />
                </label>
                <label>
                  <span>{t('onlineLastName')}</span>
                  <input value={asking.lastName} onChange={(e) => set({ lastName: e.target.value })} dir="auto" maxLength={100} />
                </label>
              </div>
            )}
            <label className={styles.pick}>
              <input type="radio" name="online-who" checked={choice === 'walkin'} onChange={() => set({ choice: 'walkin' })} />
              <span>
                <strong>{t('walkInCustomer')}</strong>
                <span className={styles.hint}>{t('onlineWalkInHint')}</span>
              </span>
            </label>
          </fieldset>
          {asking.error && (
            <p role="alert" className={styles.error}>
              {asking.error}
            </p>
          )}
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={() => setAsking(null)} disabled={busy !== null}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button variant="primary" onClick={() => void confirmWho()} loading={busy !== null}>
            {t('onlineLoad')}
          </Button>
        </ModalFooter>
      </Modal>
    )
  }

  const data = orders.data
  return (
    <Modal onClose={onClose} width={620} title={t('onlineOrders')}>
      <ModalBody>
        {orders.isPending && <Loader />}
        {data && !data.connected && <p className={styles.plain}>{t('onlineNotConnected')}</p>}
        {data && data.connected && !data.reachable && (
          <div role="alert" className={styles.notice}>
            <span>{t('onlineOffline')}</span>
            <Button onClick={() => void orders.refetch()}>{t('priceRetry')}</Button>
          </div>
        )}
        {data && data.reachable && data.orders.length === 0 && <p className={styles.plain}>{t('onlineNone')}</p>}
        <ul className={styles.list}>
          {data?.orders.map((order) => (
            <li key={order.id} className={styles.order} data-status={order.status}>
              <div className={styles.head}>
                <strong className={styles.number} dir="ltr">#{order.number}</strong>
                <span className={styles.status} data-status={order.status}>{t(`onlineStatus_${order.status}`)}</span>
                <span className={styles.spacer} />
                <span className={styles.hint}>{formatDateTime(order.createdAtUtc)}</span>
              </div>
              <p className={styles.who}>
                <strong dir="auto">{order.name}</strong>
                {[order.place, order.orderType, order.phone].filter(Boolean).map((part) => (
                  <span key={part} dir="auto"> · {part}</span>
                ))}
              </p>
              <ul className={styles.lines}>
                {order.lines.map((line, n) => (
                  <li key={n} data-missing={line.posProductId === null || undefined}>
                    <span className={styles.qty} dir="ltr">{line.quantity}×</span>
                    <span dir="auto">{line.name}</span>
                    <span className={styles.lineTotal} dir="ltr">{line.total}</span>
                  </li>
                ))}
              </ul>
              {order.note && <p className={styles.note} dir="auto">{order.note}</p>}
              <div className={styles.foot}>
                <span className={styles.hint}>
                  {t('onlineMenuTotal')} <strong dir="ltr">{order.total}</strong>
                </span>
                <span className={styles.spacer} />
                {order.status !== 'ready' && (
                  <Button variant="danger" onClick={() => void reject(order)} disabled={busy !== null}>
                    {t('onlineReject')}
                  </Button>
                )}
                {order.status === 'accepted' && (
                  <Button onClick={() => void step(order, 'ready')} disabled={busy !== null}>
                    {t('onlineReady')}
                  </Button>
                )}
                {order.status === 'ready' && (
                  <Button variant="primary" onClick={() => void step(order, 'done')} loading={busy === order.id}>
                    {t('onlineDone')}
                  </Button>
                )}
                {order.status !== 'ready' && (
                  <Button variant="primary" onClick={() => void load(order)} loading={busy === order.id} disabled={busy !== null && busy !== order.id}>
                    {t('onlineLoad')}
                  </Button>
                )}
              </div>
            </li>
          ))}
        </ul>
      </ModalBody>
      <ModalFooter>
        <Button variant="secondary" onClick={onClose}>
          {t('common.close', { ns: 'common' })}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
