import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { toast } from '@/ui/toast'
import { Icon } from '@/ui/Icon'
import { ScanInput } from '@/ui/ScanInput'
import { useMediaQuery } from '@/shell/useMediaQuery'
import { lookupBarcode } from '../catalog/api'
import { posBus } from '../slots'
import { isTypingQuantityPrefix, parseScanInput, rejectBarcode, type RejectedBarcode } from './scanLogic'
import { useScanFocus } from './useScanFocus'
import styles from './scan.module.css'

const ERROR_MS = 2500

const REJECTION_KEYS: Record<RejectedBarcode, string> = {
  url: 'urlDetected',
  contact: 'qrCodeDetected',
  structured: 'qrCodeDetected',
  too_long: 'inputTooLong',
  multiline: 'qrCodeDetected',
  special: 'qrCodeDetected',
}

/**
 * The barcode box at the top of the cart column. Enter (a scanner's terminator) looks the code up and hands the product to the cart.
 * "5*" then a scan sells five: the quantity is remembered for the next lookup only. Anything that is clearly not a product barcode
 * (a QR code with a link, a vCard ...) is refused without touching the server.
 */
export function ScanBar() {
  const { t } = useTranslation('pos')
  const inputRef = useRef<HTMLInputElement>(null)
  const [value, setValue] = useState('')
  const [pendingQuantity, setPendingQuantity] = useState<number | null>(null)
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const errorTimer = useRef(0)
  // The large box reserves 156px for its "scanner ready" pill; on a narrow cart column the small one leaves room for the placeholder.
  const compact = useMediaQuery('(max-width: 1100px)')

  useScanFocus(inputRef)
  useEffect(() => () => window.clearTimeout(errorTimer.current), [])

  const showError = (message: string) => {
    window.clearTimeout(errorTimer.current)
    setError(message)
    errorTimer.current = window.setTimeout(() => setError(''), ERROR_MS)
  }

  const submit = async () => {
    const parsed = parseScanInput(value)
    setValue('')
    if (parsed.kind === 'empty') return

    if (parsed.kind === 'quantity') {
      setPendingQuantity(parsed.quantity)
      toast.info(t('quantitySetTo', { quantity: parsed.quantity }))
      return
    }

    const rejected = rejectBarcode(parsed.barcode)
    if (rejected) {
      showError(t(REJECTION_KEYS[rejected]))
      return
    }

    const quantity = parsed.quantity > 1 ? parsed.quantity : (pendingQuantity ?? 1)
    setPendingQuantity(null)
    setBusy(true)
    try {
      const product = await lookupBarcode(parsed.barcode)
      posBus.addProduct({ product, quantity, source: 'scan' })
      setError('')
    } catch (cause) {
      showError(isApiError(cause) && cause.code === 'product_not_found' ? t('productNotFoundShort') : isApiError(cause) ? cause.message : t('errorFetchingProduct'))
    } finally {
      setBusy(false)
      posBus.focusScan()
    }
  }

  return (
    <div className={styles.scanBar} data-prefix={isTypingQuantityPrefix(value) || undefined} data-compact={compact || undefined}>
      <div className={styles.scanField}>
        <ScanInput
          ref={inputRef}
          size={compact ? 'sm' : 'lg'}
          value={value}
          onChange={setValue}
          onEnter={() => void submit()}
          placeholder={`${t('ScanBarcode')}…`}
          aria-label={t('ScanBarcode')}
          error={error}
        />
      </div>
      {pendingQuantity !== null && (
        <button type="button" className={styles.pending} onClick={() => setPendingQuantity(null)} title={t('clearPendingQuantity')}>
          <span>{t('nextScanQuantity', { quantity: pendingQuantity })}</span>
          <Icon name="close" size={12} />
        </button>
      )}
      <button type="button" className={styles.addBtn} aria-label={t('AddItem')} disabled={busy || value.trim() === ''} onClick={() => void submit()}>
        <Icon name="plus" size={18} />
      </button>
    </div>
  )
}
