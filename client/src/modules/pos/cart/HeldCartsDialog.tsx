import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { IconButton } from '@/ui/IconButton'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { formatDateTime } from '@/ui/format'
import { toast } from '@/ui/toast'
import { useCartStore } from '../store/cartStore'
import { fetchCurrencies, useDeleteAllHeldCarts, useDeleteHeldCart, useHeldCarts, useHoldCart, type HeldCart } from './api'
import { money } from './cartUi'
import { autoSaveNote, resumeHeldCart } from './heldCarts'
import { buildCartInput } from './pricing'
import styles from './cart.module.css'

interface Props {
  notation: string
  onClose: () => void
}

/**
 * The held carts of the shop (the old "Retrieve" dialog). Picking one puts it back on the till; if the till is not empty the
 * cashier chooses between parking the current cart first, throwing it away, or cancelling.
 */
export function HeldCartsDialog({ notation, onClose }: Props) {
  const { t } = useTranslation('pos')
  const held = useHeldCarts()
  const remove = useDeleteHeldCart()
  const removeAll = useDeleteAllHeldCarts()
  const hold = useHoldCart()
  const [busy, setBusy] = useState(false)
  const [asking, setAsking] = useState<HeldCart | null>(null)

  const load = async (cart: HeldCart) => {
    setBusy(true)
    try {
      const { loaded, skipped } = await resumeHeldCart(cart.id)
      toast.success(`${t('cartLoadedSuccessfully')} ${loaded} ${t('items')}`)
      for (const name of skipped) toast.warning(t('heldLineUnavailable', { name }))
      onClose()
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('failedToLoadCart'))
    } finally {
      setBusy(false)
    }
  }

  const pick = (cart: HeldCart) => {
    if (useCartStore.getState().lines.length > 0) setAsking(cart)
    else void load(cart)
  }

  /** "Save current": park what is on the till as a walk-in cart (as the old till did), then load the other one. */
  const saveThenLoad = async (cart: HeldCart) => {
    const { lines, discount, currencyId } = useCartStore.getState()
    setBusy(true)
    try {
      const rates = await fetchCurrencies()
      if (currencyId !== null) {
        await hold.mutateAsync({ cart: buildCartInput({ lines, discount }, currencyId, rates), customerId: null, notes: autoSaveNote() })
        toast.success(t('currentCartSaved'))
      }
    } catch {
      toast.warning(t('failedToSaveCurrentCart'))
    } finally {
      setBusy(false)
    }
    setAsking(null)
    await load(cart)
  }

  const deleteOne = async (cart: HeldCart) => {
    const ok = await confirm({
      title: t('deleteHeldCart'),
      message: t('actionCannotBeUndone'),
      confirmLabel: t('yesDelete'),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(cart.id)
      },
    })
    if (ok) toast.success(t('cartDeletedSuccessfully'))
  }

  const deleteAll = async () => {
    const ok = await confirm({
      title: t('deleteAllHeldCarts'),
      message: t('allHeldCartsWillBeDeleted'),
      confirmLabel: t('yesDeleteAll'),
      danger: true,
      onConfirm: async () => {
        await removeAll.mutateAsync()
      },
    })
    if (ok) toast.success(t('allCartsDeletedSuccessfully'))
  }

  if (asking) {
    return (
      <Modal onClose={() => setAsking(null)} width={440} title={t('saveCurrentCart')}>
        <ModalBody>
          <p className={styles.plain}>{t('saveBeforeLoadingHeld')}</p>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={() => setAsking(null)} disabled={busy}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button variant="danger" onClick={() => void load(asking)} disabled={busy}>
            {t('noDiscardCurrent')}
          </Button>
          <Button variant="primary" onClick={() => void saveThenLoad(asking)} loading={busy}>
            {t('yesSaveCurrent')}
          </Button>
        </ModalFooter>
      </Modal>
    )
  }

  return (
    <Modal onClose={onClose} width={520} title={t('heldCarts')}>
      <ModalBody>
        {held.isPending && <Loader />}
        {held.isError && (
          <div role="alert" className={styles.notice} data-tone="error">
            <span>{t('failedToLoadCart')}</span>
            <Button onClick={() => void held.refetch()}>{t('priceRetry')}</Button>
          </div>
        )}
        {held.data?.length === 0 && <p className={styles.plain}>{t('noHeldCartsFound')}</p>}
        <ul className={styles.heldList}>
          {held.data?.map((cart) => (
            <li key={cart.id} className={styles.heldRow}>
              <button type="button" className={styles.heldMain} onClick={() => pick(cart)} disabled={busy}>
                <strong>{cart.isWalkIn ? t('walkInCustomer') : cart.customerName}</strong>
                <span className={styles.hint}>
                  {cart.itemCount} {t('items')} · {formatDateTime(cart.createdAt)}
                </span>
                {cart.notes && <span className={styles.heldNotes}>{cart.notes}</span>}
              </button>
              <strong className={styles.heldTotal} dir="ltr">
                {money(cart.totalAmount, notation)}
              </strong>
              <IconButton icon="trash" tone="danger" label={t('deleteHeldCart')} onClick={() => void deleteOne(cart)} disabled={busy} />
            </li>
          ))}
        </ul>
      </ModalBody>
      <ModalFooter>
        {held.data && held.data.length > 0 && (
          <Button variant="danger" onClick={() => void deleteAll()} disabled={busy}>
            {t('DeleteAll')}
          </Button>
        )}
        <Button variant="secondary" onClick={onClose}>
          {t('common.close', { ns: 'common' })}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
