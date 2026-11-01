import type { Download } from '@/api/http'

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
