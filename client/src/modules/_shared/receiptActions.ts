import { useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { http, isApiError, type Download } from '@/api/http'
import { confirm } from '@/ui/confirm'
import { toast } from '@/ui/toast'

/** Hands a generated file (CSV, PDF) to the browser's download bar. */
export function saveDownload(file: Download, fallbackName: string) {
  const url = URL.createObjectURL(file.blob)
  const link = document.createElement('a')
  link.href = url
  link.download = file.filename || fallbackName
  link.style.display = 'none'
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

interface PrintResult {
  printed: boolean
  message: string
}

/**
 * Receipt-printer printing for the back office (D11: the server prints and builds the PDF, the browser only asks). A reprint goes straight
 * to the receipt printer; when the printer cannot be reached the answer says so and offers the same receipt as a PDF instead.
 * A second click while a job is running is ignored, so a slow printer cannot produce two slips.
 *
 * `printPath` / `pdfPath` are API paths without the `/api/v1` prefix, e.g. `/receipts/5/print` and `/receipts/5/pdf`.
 */
export function useReceiptPrinting() {
  const { t } = useTranslation('print')
  const running = useRef(false)
  const [busy, setBusy] = useState(false)

  const downloadPdf = async (pdfPath: string, fallbackName = 'receipt.pdf') => {
    try {
      saveDownload(await http.download(pdfPath), fallbackName)
    } catch (error) {
      toast.error(isApiError(error) ? error.message : t('pdfFailed', 'Could not create the PDF.'))
    }
  }

  /** Resolves to whether the receipt reached the printer. */
  const print = async (printPath: string, pdfPath: string, fallbackName?: string): Promise<boolean> => {
    if (running.current) return false
    running.current = true
    setBusy(true)
    try {
      const result = await http.post<PrintResult>(printPath)
      if (result.printed) {
        toast.success(result.message, 2500)
        return true
      }
      const download = await confirm({
        title: t('title', 'Printing'),
        message: result.message,
        confirmLabel: t('downloadInstead', 'Download PDF instead'),
      })
      if (download) await downloadPdf(pdfPath, fallbackName)
      return false
    } catch (error) {
      // 404 (the receipt is gone) and the like: the http client has already raised its toast; only a network failure needs saying here.
      if (!isApiError(error)) toast.error(t('failed', 'Could not print. Check that the receipt printer is connected and selected in Settings.'))
      return false
    } finally {
      running.current = false
      setBusy(false)
    }
  }

  return { print, downloadPdf, busy }
}
