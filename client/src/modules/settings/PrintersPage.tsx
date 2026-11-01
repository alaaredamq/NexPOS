import { useEffect, useMemo, useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { electron, type PrinterInfo } from '@/core/electron'
import { Button } from '@/ui/Button'
import { Field } from '@/ui/form/Field'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Loader } from '@/ui/Loader'
import { Select } from '@/ui/Select'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { getPrinterName, setPrinterName } from '../products/labels/stickerLabel'
import { useInstalledPrinters, usePrintingSettings, useSavePrintingSettings, useTestPrinter, type PrintingSettings } from './api'
import { SettingsLayout } from './SettingsLayout'
import styles from './settings.module.css'

// The limits of the sticker size (the server enforces the same ones).
const WIDTH = { min: 15, max: 150 } as const
const HEIGHT = { min: 10, max: 150 } as const

interface FormValues {
  receiptPrinterName: string | null
  stickerWidthMm: number | null
  stickerHeightMm: number | null
}

/** `/app/settings/printers` - the receipt printer (with a test slip), the barcode sticker size and this computer's sticker printer. */
export default function PrintersPage() {
  const query = usePrintingSettings()

  return (
    <SettingsLayout>
      {query.isPending && <Loader fullSection />}
      {query.isError && <p className={styles.error}>{query.error.message}</p>}
      {query.data && <PrintersForm initial={query.data} />}
    </SettingsLayout>
  )
}

const toValues = (s: PrintingSettings): FormValues => ({
  receiptPrinterName: s.receiptPrinterName || null,
  stickerWidthMm: s.stickerWidthMm,
  stickerHeightMm: s.stickerHeightMm,
})

function PrintersForm({ initial }: { initial: PrintingSettings }) {
  const { t } = useTranslation(['settings', 'common'])
  const save = useSavePrintingSettings()
  const test = useTestPrinter()
  const installed = useInstalledPrinters()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        receiptPrinterName: z.string().max(200).nullable(),
        stickerWidthMm: z
          .number({ error: t('StickerWidthRange', { ns: 'settings', ...WIDTH }) })
          .nullable()
          .refine((v) => v !== null && v >= WIDTH.min && v <= WIDTH.max, t('StickerWidthRange', { ns: 'settings', ...WIDTH })),
        stickerHeightMm: z
          .number({ error: t('StickerHeightRange', { ns: 'settings', ...HEIGHT }) })
          .nullable()
          .refine((v) => v !== null && v >= HEIGHT.min && v <= HEIGHT.max, t('StickerHeightRange', { ns: 'settings', ...HEIGHT })),
      }),
    [t],
  )

  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema as never), defaultValues: toValues(initial) })
  const chosen = useWatch({ control: form.control, name: 'receiptPrinterName' })

  // A configured printer that is unplugged right now stays selectable, so saving does not silently drop it.
  const options = useMemo(() => {
    const names = installed.data ?? []
    const list = names.map((name) => ({ value: name, label: name }))
    const current = initial.receiptPrinterName
    if (current && !names.includes(current)) list.push({ value: current, label: `${current} ${t('Set_NotDetected', { ns: 'common' })}` })
    return list
  }, [installed.data, initial.receiptPrinterName, t])

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      const saved = await save.mutateAsync({
        receiptPrinterName: values.receiptPrinterName ?? '',
        stickerWidthMm: values.stickerWidthMm as number,
        stickerHeightMm: values.stickerHeightMm as number,
      })
      form.reset(toValues(saved))
      toast.success(t('PrintingSaved', { ns: 'settings' }))
    } catch (error) {
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  const runTest = async () => {
    if (!chosen) {
      toast.warning(t('Set_SelectPrinterFirst', { ns: 'common' }))
      return
    }
    try {
      const result = await test.mutateAsync(chosen)
      if (result.printed) toast.success(result.message)
      else toast.error(result.message)
    } catch {
      toast.error(t('Set_TestFailed', { ns: 'common' }))
    }
  }

  return (
    <form onSubmit={submit} noValidate className={styles.stack}>
      <p className={styles.note}>{t('Set_PrintersSubtitle', { ns: 'common' })}</p>
      <FormError messages={formErrors} />

      <section className={styles.card} aria-labelledby="receipt-printer">
        <h2 id="receipt-printer" className={styles.cardTitle}>
          {t('Set_ReceiptPrinter', { ns: 'common' })}
        </h2>
        <div className={styles.inline}>
          <SelectField<FormValues, string>
            control={form.control}
            name="receiptPrinterName"
            label={t('Set_ReceiptPrinter', { ns: 'common' })}
            hint={t('Set_ReceiptPrinterHelp', { ns: 'common' })}
            options={options}
            placeholder={t('Set_SelectPrinter', { ns: 'common' })}
            clearable
          />
          <Button onClick={() => void runTest()} loading={test.isPending} title={t('Set_TestTooltip', { ns: 'common' })}>
            {t('Set_Test', { ns: 'common' })}
          </Button>
          <Button variant="ghost" onClick={() => void installed.refetch()} loading={installed.isFetching}>
            {t('RefreshPrinters', { ns: 'settings' })}
          </Button>
        </div>
        {installed.isError && <p className={styles.error}>{t('PrintersLoadFailed', { ns: 'settings' })}</p>}
      </section>

      <section className={styles.card} aria-labelledby="stickers">
        <h2 id="stickers" className={styles.cardTitle}>
          {t('Set_BarcodeStickers', { ns: 'common' })}
        </h2>
        <StickerPrinter />
        <div className={styles.row}>
          <NumberField<FormValues> control={form.control} name="stickerWidthMm" label={t('Set_StickerWidth', { ns: 'common' })} decimals={0} min={WIDTH.min} max={WIDTH.max} required />
          <NumberField<FormValues> control={form.control} name="stickerHeightMm" label={t('Set_StickerHeight', { ns: 'common' })} decimals={0} min={HEIGHT.min} max={HEIGHT.max} required />
        </div>
      </section>

      <div className={styles.actions}>
        <Button type="submit" variant="primary" loading={save.isPending} disabled={!form.formState.isDirty}>
          {t('Set_SaveSettings', { ns: 'common' })}
        </Button>
      </div>
    </form>
  )
}

/**
 * The sticker printer is a per-computer choice kept in this browser's storage (several tills can share one database), saved the moment it is
 * picked, and listed by the desktop app only - in a plain browser the label prints through the browser's own dialog.
 */
function StickerPrinter() {
  const { t } = useTranslation('common')
  const desktop = electron.isElectron
  const [printers, setPrinters] = useState<PrinterInfo[]>([])
  const [current, setCurrent] = useState(getPrinterName)

  useEffect(() => {
    if (!desktop) return
    let live = true
    void electron.listPrinters().then((list) => live && setPrinters(list))
    return () => {
      live = false
    }
  }, [desktop])

  const options = printers.map((p) => ({ value: p.name, label: p.displayName || p.name }))
  // Keep a printer that is saved but not listed right now selectable.
  if (current && !options.some((o) => o.value === current)) options.push({ value: current, label: `${current} ${t('Set_NotDetected')}` })
  const selected = options.find((o) => o.value === current) ?? null

  return (
    <Field label={t('Set_StickerPrinter')} hint={desktop ? t('Set_StickerPrinterHelp') : t('Set_StickerDesktopOnly')}>
      {(a11y) => (
        <Select<string>
          {...a11y}
          value={selected}
          options={options}
          placeholder={t('Set_SystemDefault')}
          clearable
          disabled={!desktop}
          onChange={(option) => {
            const name = option?.value ?? ''
            setPrinterName(name)
            setCurrent(name)
            toast.success(t('Set_StickerSaved'))
          }}
        />
      )}
    </Field>
  )
}
