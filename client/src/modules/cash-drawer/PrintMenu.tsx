import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { toast } from '@/ui/toast'
import { downloadZReportPdf, printZReport } from './api'
import styles from './cashDrawer.module.css'

/**
 * Print the Z report on the receipt printer, or save it as a PDF (D11: both are made on the server). The printer
 * request never fails because of the printer - `printed: false` carries the server's message, and the PDF is the fallback.
 */
export function PrintMenu({ sessionId }: { sessionId: number }) {
  const { t } = useTranslation(['cashDrawer', 'common'])
  const [busy, setBusy] = useState<'print' | 'pdf' | null>(null)

  const run = async (kind: 'print' | 'pdf') => {
    setBusy(kind)
    try {
      if (kind === 'pdf') {
        await downloadZReportPdf(sessionId)
      } else {
        const result = await printZReport(sessionId)
        if (result.printed) toast.success(result.message)
        else toast.error(result.message)
      }
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('common.actionFailed', { ns: 'common' }))
    } finally {
      setBusy(null)
    }
  }

  return (
    <div className={`${styles.actions} ${styles.noPrint}`}>
      <Button variant="primary" onClick={() => void run('print')} loading={busy === 'print'} disabled={busy === 'pdf'}>
        {t('Print_ReceiptPaper', { ns: 'common' })}
      </Button>
      <Button onClick={() => void run('pdf')} loading={busy === 'pdf'} disabled={busy === 'print'}>
        {t('Print_DownloadPdf', { ns: 'common' })}
      </Button>
    </div>
  )
}
