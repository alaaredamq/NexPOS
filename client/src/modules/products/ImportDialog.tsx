import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { productsApi, saveDownload, useInvalidateProducts, type ProductImportResult } from './api'
import styles from './ImportDialog.module.css'

type Stage = 'check' | 'import' | 'done'

/**
 * Excel import in two steps, like the old dialog: the first click checks the file (nothing is saved), the second one imports it.
 * Rows with errors are skipped and listed; existing barcodes are updated, new ones created. The workbook's Custom Prices
 * sheet carries each product's extra prices, one row per price.
 */
export function ImportDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('products')
  const invalidate = useInvalidateProducts()
  const input = useRef<HTMLInputElement>(null)
  const [file, setFile] = useState<File | null>(null)
  const [result, setResult] = useState<ProductImportResult | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [stage, setStage] = useState<Stage>('check')
  const [busy, setBusy] = useState(false)

  const changes = result ? result.created + result.updated : 0

  const run = async (apply: boolean) => {
    if (!file) return
    setBusy(true)
    setError(null)
    try {
      const res = await productsApi.import(file, apply)
      setResult(res)
      if (res.fileError) {
        setStage('check')
      } else if (apply) {
        setStage('done')
        void invalidate()
      } else {
        setStage(res.created + res.updated > 0 ? 'import' : 'check')
      }
    } catch (e) {
      setResult(null)
      setError(isApiError(e) ? e.message : t('Prod_ImportFailed'))
    } finally {
      setBusy(false)
    }
  }

  const label =
    stage === 'import' ? `${t('Prod_Import')} (${changes})` : stage === 'done' ? t('Prod_Import') : t('Prod_ImportCheck')

  const downloadTemplate = async () => {
    try {
      saveDownload(await productsApi.importTemplate(), 'products_import_template.xlsx')
    } catch (e) {
      toast.error(isApiError(e) ? e.message : t('Prod_ImportFailed'))
    }
  }

  return (
    <Modal title={t('Prod_ImportTitle')} width={640} onClose={onClose}>
      <ModalBody>
        <p className={styles.help}>{t('Prod_ImportHelp')}</p>
        <button type="button" className={styles.template} onClick={() => void downloadTemplate()}>
          {t('Prod_ImportTemplate')}
        </button>
        <input
          ref={input}
          type="file"
          className={styles.file}
          accept=".xlsx,.csv,.txt,text/csv,application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
          aria-label={t('Prod_ImportTitle')}
          onChange={(e) => {
            setFile(e.target.files?.[0] ?? null)
            setResult(null)
            setError(null)
            setStage('check')
          }}
        />

        <div aria-live="polite">
          {error && <p className={styles.error}>{error}</p>}
          {result?.fileError && <p className={styles.error}>{result.fileError}</p>}
          {result && !result.fileError && (
            <>
              <div className={styles.stats}>
                <Stat label={t('Prod_ImportCreated')} value={result.created} tone="success" />
                <Stat label={t('Prod_ImportUpdated')} value={result.updated} tone="primary" />
                <Stat label={t('Prod_ImportUnchanged')} value={result.unchanged} />
                <Stat label={t('Prod_ImportSkipped')} value={result.failed} tone={result.failed ? 'danger' : undefined} />
                {result.newCategories > 0 && <Stat label={t('Prod_ImportNewCategories')} value={result.newCategories} />}
                {result.customPrices > 0 && <Stat label={t('Prod_ImportCustomPrices')} value={result.customPrices} />}
              </div>
              <p className={styles.note} data-kind={result.applied ? 'success' : changes === 0 ? 'warning' : 'info'}>
                {result.applied
                  ? t('Prod_ImportDone')
                  : changes === 0
                    ? t('Prod_ImportNothing')
                    : t('Prod_ImportPreviewNote') + (result.failed ? ` ${t('Prod_ImportSkipNote')}` : '')}
              </p>
              {result.issues.length > 0 && (
                <ul className={styles.issues}>
                  {result.issues.map((issue) => (
                    <li key={`${issue.sheet ?? ''}-${issue.row}-${issue.message}`}>
                      <strong>{`${issue.sheet ? `${issue.sheet} · ` : ''}${t('Prod_ImportRow')} ${issue.row}: `}</strong>
                      {issue.message}
                    </li>
                  ))}
                </ul>
              )}
            </>
          )}
        </div>
      </ModalBody>
      <ModalFooter>
        <Button onClick={onClose}>{t('Prod_ImportClose')}</Button>
        <Button variant="primary" loading={busy} disabled={!file || stage === 'done'} onClick={() => void run(stage === 'import')}>
          {label}
        </Button>
      </ModalFooter>
    </Modal>
  )
}

function Stat({ label, value, tone }: { label: string; value: number; tone?: 'success' | 'primary' | 'danger' }) {
  return (
    <div className={styles.stat}>
      <div className={`${styles.statValue} tabular-nums`} data-tone={tone}>
        {value}
      </div>
      <div className={styles.statLabel}>{label}</div>
    </div>
  )
}
