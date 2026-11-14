import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { Field } from '@/ui/form/Field'
import { NumericInput } from '@/ui/form/NumericInput'
import { toast } from '@/ui/toast'
import { electron, type PrinterInfo } from '@/core/electron'
import { useStickerSettings } from '../api'
import {
  COPY_PRESETS,
  DEFAULT_LABEL_SIZE,
  MAX_COPIES,
  MAX_HEIGHT_MM,
  MAX_WIDTH_MM,
  MIN_HEIGHT_MM,
  MIN_WIDTH_MM,
  buildBarcodeSvg,
  clampCopies,
  clampHeight,
  clampWidth,
  getDirectPrint,
  getPrinterName,
  getShowPrice,
  printSticker,
  setDirectPrint,
  setPrinterName,
  setShowPrice,
  type StickerData,
} from './stickerLabel'
import styles from './StickerLabelDialog.module.css'

interface DialogProps {
  data: StickerData
  onClose: () => void
}

/** The "Print label" dialog: printer (desktop only), price on/off, size in mm and number of copies. */
export function StickerLabelDialog({ data, onClose }: DialogProps) {
  const { t } = useTranslation('products')
  const settings = useStickerSettings()
  const desktop = electron.isElectron

  const [width, setWidth] = useState<number | null>(null)
  const [height, setHeight] = useState<number | null>(null)
  const [copies, setCopies] = useState<number | null>(1)
  const [showPrice, setShowPriceState] = useState(getShowPrice)
  const [printer, setPrinter] = useState(getPrinterName)
  const [printers, setPrinters] = useState<PrinterInfo[]>([])
  const [directChoice, setDirectChoice] = useState<boolean | null>(null)
  const [busy, setBusy] = useState(false)

  // The shop's label size is the starting point; typing over it is a one-off for this job.
  const sizeWidth = width ?? clampWidth(settings.data?.widthMm, DEFAULT_LABEL_SIZE.widthMm)
  const sizeHeight = height ?? clampHeight(settings.data?.heightMm, DEFAULT_LABEL_SIZE.heightMm)
  const count = clampCopies(copies)
  // Empty = the OS default printer; direct printing needs its real name.
  const printerName = printer || printers.find((p) => p.isDefault)?.name || ''
  const direct = desktop && printerName !== '' && (directChoice ?? getDirectPrint(printerName))

  useEffect(() => {
    if (!desktop) return
    let live = true
    void electron.listPrinters().then((list) => live && setPrinters(list))
    return () => {
      live = false
    }
  }, [desktop])

  const print = async () => {
    setBusy(true)
    try {
      await printSticker(data, {
        copies: count,
        widthMm: clampWidth(sizeWidth),
        heightMm: clampHeight(sizeHeight),
        showPrice,
        printerName: direct ? printerName : printer,
        direct,
      })
      onClose()
    } catch (error) {
      toast.error(`${t('printLabel.failed')}: ${error instanceof Error ? error.message : String(error)}`)
      setBusy(false)
    }
  }

  const systemDefault = t('printLabel.systemDefault')
  const remembered = printer && !printers.some((p) => p.name === printer)

  return (
    <Modal title={t('printLabel.title')} width={440} onClose={onClose}>
      <ModalBody>
        <div className={styles.summary}>
          <div className={styles.name}>{data.name || ' '}</div>
          <div className={styles.meta} dir="ltr">
            {data.code}
            {data.price ? ` · ${data.price}` : ''}
          </div>
        </div>

        {desktop && (
          <Field label={t('printLabel.printer')}>
            {(a11y) => (
              <select
                {...a11y}
                className={styles.select}
                value={printer}
                onChange={(e) => {
                  setPrinter(e.target.value)
                  setPrinterName(e.target.value)
                }}
              >
                <option value="">{systemDefault}</option>
                {printers.map((p) => (
                  <option key={p.name} value={p.name}>
                    {(p.displayName || p.name) + (p.isDefault ? ` (${systemDefault})` : '')}
                  </option>
                ))}
                {/* Keep a remembered printer selectable even if it isn't connected right now. */}
                {remembered && <option value={printer}>{`${printer} (${t('printLabel.notDetected')})`}</option>}
              </select>
            )}
          </Field>
        )}

        {desktop && printerName && (
          <label className={styles.check}>
            <input
              type="checkbox"
              checked={direct}
              onChange={(e) => {
                setDirectChoice(e.target.checked)
                setDirectPrint(e.target.checked)
              }}
            />
            <span>{t('printLabel.direct')}</span>
          </label>
        )}
        {desktop && printerName && <div className={styles.hint}>{t('printLabel.directHint')}</div>}

        {data.price && (
          <label className={styles.check}>
            <input
              type="checkbox"
              checked={showPrice}
              onChange={(e) => {
                setShowPriceState(e.target.checked)
                setShowPrice(e.target.checked)
              }}
            />
            <span>{t('printLabel.showPrice')}</span>
          </label>
        )}

        <div className={styles.group}>{t('printLabel.size')}</div>
        <div className={styles.size}>
          <Field label={t('printLabel.width')}>
            {(a11y) => (
              <NumericInput {...a11y} value={sizeWidth} onChange={setWidth} decimals={0} min={MIN_WIDTH_MM} max={MAX_WIDTH_MM} suffix="mm" />
            )}
          </Field>
          <span className={styles.times} aria-hidden="true">
            ×
          </span>
          <Field label={t('printLabel.height')}>
            {(a11y) => (
              <NumericInput {...a11y} value={sizeHeight} onChange={setHeight} decimals={0} min={MIN_HEIGHT_MM} max={MAX_HEIGHT_MM} suffix="mm" />
            )}
          </Field>
        </div>

        <div className={styles.group}>{t('printLabel.copies')}</div>
        <div className={styles.copies} dir="ltr">
          <Button aria-label={t('printLabel.decrease')} disabled={count <= 1} onClick={() => setCopies(count - 1)}>
            −
          </Button>
          <NumericInput
            value={copies}
            onChange={setCopies}
            onBlur={() => setCopies(count)}
            decimals={0}
            min={1}
            max={MAX_COPIES}
            selectOnFocus
            aria-label={t('printLabel.copies')}
          />
          <Button aria-label={t('printLabel.increase')} disabled={count >= MAX_COPIES} onClick={() => setCopies(count + 1)}>
            +
          </Button>
        </div>
        <div className={styles.presets}>
          {COPY_PRESETS.map((n) => (
            <Button key={n} variant={n === count ? 'primary' : 'secondary'} onClick={() => setCopies(n)}>
              {n}
            </Button>
          ))}
        </div>
        <div className={styles.hint}>{t('printLabel.hint', { max: MAX_COPIES })}</div>
      </ModalBody>
      <ModalFooter>
        <Button onClick={onClose}>{t('Cancel', { ns: 'common' })}</Button>
        <Button variant="primary" loading={busy} onClick={() => void print()} data-autofocus>
          {/* A plain browser can't reach a label printer, so there the button saves a PDF. */}
          {desktop ? t('printLabel.print', { count }) : t('printLabel.downloadPdf', { count })}
        </Button>
      </ModalFooter>
    </Modal>
  )
}

/**
 * `const label = useStickerLabel()` ... `label.open({ name, code, price })` ... `{label.dialog}`.
 * Checks the barcode first (empty, or something Code 128 cannot encode) so the dialog never opens for a label that cannot print.
 */
export function useStickerLabel(): { open: (data: StickerData) => Promise<void>; dialog: ReactNode } {
  const { t } = useTranslation('products')
  const [data, setData] = useState<StickerData | null>(null)

  const open = useCallback(
    async (input: StickerData) => {
      const code = input.code.trim()
      if (!code) {
        toast.warning(t('printLabel.missingBarcode'))
        return
      }
      try {
        await buildBarcodeSvg(code)
      } catch (error) {
        toast.error(error instanceof Error ? error.message : String(error))
        return
      }
      setData({ name: input.name.trim(), code, price: input.price })
    },
    [t],
  )

  return { open, dialog: data ? <StickerLabelDialog data={data} onClose={() => setData(null)} /> : null }
}
