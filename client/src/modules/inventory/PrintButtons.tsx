import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { toast } from '@/ui/toast'
import { downloadReportPdf, printReport, type ReportName } from './api'
import styles from './inventory.module.css'

/**
 * "Print on the receipt printer" and "Download PDF" for a report, from the same server document. A printer that cannot be
 * reached is not an error page: `failure` carries the server's message so the page can show it with the PDF one click away
 * (what the old toast offered).
 */
export function useReportPrinting(report: ReportName) {
  const { t } = useTranslation('print')
  const [busy, setBusy] = useState<'print' | 'pdf' | null>(null)
  const [failure, setFailure] = useState<string | null>(null)

  const run = async (action: 'print' | 'pdf') => {
    setFailure(null)
    setBusy(action)
    try {
      if (action === 'print') {
        const result = await printReport(report)
        if (result.printed) toast.success(t('sent'))
        else setFailure(result.message)
      } else {
        await downloadReportPdf(report)
      }
    } catch (error) {
      setFailure(error instanceof Error ? error.message : t('failed'))
    } finally {
      setBusy(null)
    }
  }

  return { busy, failure, print: () => void run('print'), pdf: () => void run('pdf') }
}

type Printing = ReturnType<typeof useReportPrinting>

export function PrintButtons({ printing }: { printing: Printing }) {
  const { t } = useTranslation('inventory')
  return (
    <>
      <Button onClick={printing.print} loading={printing.busy === 'print'} disabled={printing.busy === 'pdf'}>
        {t('PrintReceiptPaper')}
      </Button>
      <Button onClick={printing.pdf} loading={printing.busy === 'pdf'} disabled={printing.busy === 'print'}>
        {t('DownloadPdf')}
      </Button>
    </>
  )
}

/** Shown under the header after a failed print or download. */
export function PrintFailure({ printing }: { printing: Printing }) {
  const { t } = useTranslation('print')
  if (!printing.failure) return null
  return (
    <div className={styles.banner} role="alert">
      <span className={styles.bannerText}>{printing.failure}</span>
      <Button onClick={printing.print} loading={printing.busy === 'print'}>
        {t('retry')}
      </Button>
      <Button onClick={printing.pdf} loading={printing.busy === 'pdf'}>
        {t('downloadInstead')}
      </Button>
    </div>
  )
}
