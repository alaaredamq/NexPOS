import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Icon } from '@/ui/Icon'
import { toast } from '@/ui/toast'
import { posBus } from '../slots'
import { useOnlineOrders } from './api'
import { OnlineOrdersDialog } from './OnlineOrdersDialog'
import styles from './online.module.css'

/**
 * The till's "Online orders" button, in the top bar. Hidden unless this PC is connected to Online. The badge counts orders nobody has
 * answered yet, and a new one is announced with a toast, so the cashier notices it while selling.
 */
export function OnlineOrdersButton() {
  const { t } = useTranslation('pos')
  const orders = useOnlineOrders()
  const [open, setOpen] = useState(false)
  const seen = useRef<Set<number> | null>(null)

  const fresh = orders.data?.orders.filter((o) => o.status === 'new') ?? []
  const all = orders.data?.orders.length ?? 0

  useEffect(() => {
    if (!orders.data) return
    const ids = new Set(orders.data.orders.filter((o) => o.status === 'new').map((o) => o.id))
    if (seen.current) {
      const arrived = [...ids].filter((id) => !seen.current!.has(id)).length
      // Clicking the toast opens the orders, so the cashier goes straight to the new one. It stays up longer than a normal
      // info toast because the cashier may be busy with a customer when it appears.
      if (arrived > 0)
        toast.info(arrived === 1 ? t('onlineArrived') : t('onlineArrivedMany', { count: arrived }), {
          duration: 12_000,
          onClick: () => setOpen(true),
        })
    }
    seen.current = ids
  }, [orders.data, t])

  if (!orders.data?.connected) return null

  const close = () => {
    setOpen(false)
    posBus.focusScan()
  }

  return (
    <>
      <button type="button" className={styles.button} data-new={fresh.length > 0 || undefined} onClick={() => setOpen(true)} title={t('onlineOrders')}>
        <Icon name="bell" size={18} />
        <span className={styles.label}>{t('onlineOrders')}</span>
        {all > 0 && <span className={styles.count}>{fresh.length || all}</span>}
      </button>
      {open && <OnlineOrdersDialog onClose={close} />}
    </>
  )
}
