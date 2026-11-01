// Barcode sticker logic, ported from Web/wwwroot/js/sticker-label.js (REACT_MIGRATION C1). Pure functions plus the two
// print paths; the dialog is StickerLabelDialog.tsx. The print path itself is unchanged (D11):
//   - desktop app: the label is an HTML document handed to the main process, which prints silently to the chosen printer;
//   - plain browser: the barcode is encoded to bars here and the server draws the PDF (POST products/sticker-pdf).

import { http } from '@/api/http'
import { electron } from '@/core/electron'

/** Upper bound on one print job - a mistyped quantity shouldn't tie up the label printer. */
export const MAX_COPIES = 100
export const COPY_PRESETS = [1, 5, 10, 20] as const
export const MIN_WIDTH_MM = 15
export const MAX_WIDTH_MM = 150
export const MIN_HEIGHT_MM = 10
export const MAX_HEIGHT_MM = 150
export const DEFAULT_LABEL_SIZE = { widthMm: 40, heightMm: 25 } as const

// Same localStorage keys as the Razor pages: the printer is a per-computer hardware choice shared with App Settings.
const PRINTER_KEY = 'nexpos-sticker-printer'
const SHOW_PRICE_KEY = 'nexpos-sticker-show-price'

export interface StickerData {
  name: string
  code: string
  /** Already formatted (see `formatStickerPrice`); empty = nothing to print. */
  price: string
}

export interface StickerJob {
  copies: number
  widthMm: number
  heightMm: number
  showPrice: boolean
  /** Desktop only. Empty = the OS default printer. */
  printerName: string
}

/** Whole number inside [min, max], else `fallback` (a blank or nonsense field keeps the shop's default). */
export function clamp(value: unknown, min: number, max: number, fallback: number): number {
  const n = Math.floor(Number(value))
  if (!Number.isFinite(n) || n <= 0) return fallback
  return Math.min(max, Math.max(min, n))
}

export const clampCopies = (copies: unknown) => clamp(copies, 1, MAX_COPIES, 1)
export const clampWidth = (v: unknown, fallback: number = DEFAULT_LABEL_SIZE.widthMm) => clamp(v, MIN_WIDTH_MM, MAX_WIDTH_MM, fallback)
export const clampHeight = (v: unknown, fallback: number = DEFAULT_LABEL_SIZE.heightMm) => clamp(v, MIN_HEIGHT_MM, MAX_HEIGHT_MM, fallback)

/** `$` + 1234.5 -> `$1,234.50`, matching CurrencyHelper.FormatCurrency on the server (no space, Latin digits). */
export function formatStickerPrice(amount: number, notation?: string | null): string {
  const n = Number(amount) || 0
  return (notation ?? '$') + n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
}

export function escapeHtml(value: unknown): string {
  return String(value ?? '')
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

// ---- per-computer preferences -------------------------------------------------------------------------------------

export function getPrinterName(): string {
  try {
    return localStorage.getItem(PRINTER_KEY) ?? ''
  } catch {
    return ''
  }
}

export function setPrinterName(name: string): void {
  try {
    if (name) localStorage.setItem(PRINTER_KEY, name)
    else localStorage.removeItem(PRINTER_KEY)
  } catch {
    // storage blocked: falls back to the default printer
  }
}

/** Whether the price is printed on the label: remembered per computer, on unless switched off. */
export function getShowPrice(): boolean {
  try {
    return localStorage.getItem(SHOW_PRICE_KEY) !== '0'
  } catch {
    return true
  }
}

export function setShowPrice(show: boolean): void {
  try {
    localStorage.setItem(SHOW_PRICE_KEY, show ? '1' : '0')
  } catch {
    // storage blocked: not remembered
  }
}

// ---- barcode ------------------------------------------------------------------------------------------------------

// JsBarcode is ~25 KB and only the label dialog needs it: load it on first use so the products list stays small.
async function loadJsBarcode() {
  return (await import('jsbarcode')).default
}

/**
 * The barcode as modules ('1' = bar, '0' = space) for the server to draw into a PDF.
 * Throws if the code can't be encoded (e.g. non-ASCII characters).
 */
export async function buildBarcodeBars(code: string): Promise<string> {
  const JsBarcode = await loadJsBarcode()
  const encoded: { encodings?: { data: string }[] } = {}
  JsBarcode(encoded, code, { format: 'CODE128', displayValue: false })
  return (encoded.encodings ?? []).map((e) => e.data).join('')
}

/** The barcode as an inline SVG string; throws if the code can't be encoded. */
export async function buildBarcodeSvg(code: string): Promise<string> {
  const JsBarcode = await loadJsBarcode()
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg')
  JsBarcode(svg, code, { format: 'CODE128', width: 1.4, height: 34, margin: 0, displayValue: false })
  // Stretch to whatever box the label size gives it; bars scale uniformly so it still scans.
  svg.setAttribute('preserveAspectRatio', 'none')
  return svg.outerHTML
}

/**
 * One page per sticker (name / barcode / code / price). Sizes are the 40mm x 25mm layout scaled to the chosen label,
 * so small and large stock keep the same proportions. `barcodeSvg` comes from `buildBarcodeSvg`.
 */
export function buildStickerHtml(
  data: StickerData,
  barcodeSvg: string,
  widthMm: number,
  heightMm: number,
  copies: number,
  showPrice: boolean,
): string {
  const s = Math.min(widthMm / 40, heightMm / 25)
  const mm = (v: number) => `${(v * s).toFixed(2)}mm`

  const sticker =
    '  <div class="sticker">\n' +
    `    <div class="name">${escapeHtml(data.name)}</div>\n` +
    `    <div class="bc">${barcodeSvg}</div>\n` +
    `    <div class="sku">${escapeHtml(data.code)}</div>\n` +
    (data.price && showPrice ? `    <div class="price">${escapeHtml(data.price)}</div>\n` : '') +
    '  </div>'
  const stickers = Array.from({ length: clampCopies(copies) }, () => sticker)

  return (
    '<!doctype html>\n<html>\n<head>\n<meta charset="utf-8" />\n<style>\n' +
    `  @page { size: ${widthMm}mm ${heightMm}mm; margin: 0; }\n` +
    '  html, body { margin: 0; padding: 0; }\n' +
    `  body { width: ${widthMm}mm; font-family: Arial, Helvetica, sans-serif; color: #000; background: #fff; }\n` +
    '  .sticker {\n' +
    `    width: ${widthMm}mm; height: ${heightMm}mm; box-sizing: border-box; overflow: hidden;\n` +
    '    break-after: page; page-break-after: always;\n' +
    `    padding: ${mm(1.4)} ${mm(1.6)};\n` +
    '    display: flex; flex-direction: column; align-items: center; justify-content: center;\n' +
    `    gap: ${mm(0.6)}; text-align: center;\n` +
    '  }\n' +
    `  .name { font-size: ${mm(2.7)}; font-weight: 700; line-height: 1.1; max-width: 100%;\n` +
    '    white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }\n' +
    `  .bc { width: ${(widthMm * 0.85).toFixed(2)}mm; height: ${(heightMm * 0.28).toFixed(2)}mm; margin: ${mm(0.3)} 0; }\n` +
    '  .bc svg { width: 100%; height: 100%; display: block; }\n' +
    `  .sku { font-size: ${mm(1.7)}; letter-spacing: 0.06em; color: #2a2a2a; font-family: Consolas, monospace; }\n` +
    `  .price { font-size: ${mm(4)}; font-weight: 800; margin-top: ${mm(0.2)}; }\n` +
    '  /* The last label must not push out a trailing blank page. */\n' +
    '  .sticker:last-child { break-after: auto; page-break-after: auto; }\n' +
    `</style>\n</head>\n<body>\n${stickers.join('\n')}\n</body>\n</html>`
  )
}

// ---- printing -----------------------------------------------------------------------------------------------------

function saveBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = filename
  link.style.display = 'none'
  document.body.appendChild(link)
  link.click()
  link.remove()
  window.setTimeout(() => URL.revokeObjectURL(url), 10_000)
}

/**
 * Sends the job. Resolves once handed off; throws an Error (or ApiError) with a message on failure.
 * Desktop app -> silent print to the chosen printer; plain browser -> the labels are downloaded as a PDF.
 */
export async function printSticker(data: StickerData, job: StickerJob): Promise<void> {
  const copies = clampCopies(job.copies)
  const widthMm = clampWidth(job.widthMm)
  const heightMm = clampHeight(job.heightMm)

  if (electron.isElectron) {
    const html = buildStickerHtml(data, await buildBarcodeSvg(data.code), widthMm, heightMm, 1, job.showPrice)
    const result = await electron.printSticker({ html, printerName: job.printerName || undefined, copies, widthMm, heightMm })
    if (!result.success) throw new Error(result.failureReason ?? 'unknown error')
    return
  }

  const file = await http.downloadPost('/products/sticker-pdf', {
    name: data.name,
    code: data.code,
    price: data.price,
    bars: await buildBarcodeBars(data.code),
    widthMm,
    heightMm,
    copies,
    showPrice: job.showPrice,
  })
  saveBlob(file.blob, file.filename || `labels-${data.code}.pdf`)
}
