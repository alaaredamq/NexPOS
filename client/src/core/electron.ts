// Typed access to the desktop shell. electron-preload.js exposes `window.nexpos` through contextBridge; in a plain
// browser (dev server, a customer using Chrome) it does not exist, so every call here degrades to a no-op.
//
// Receipts, PDFs and A4 printing are server-side (D11) and need nothing from this file. The only native APIs are the
// sticker-label ones below. UI zoom is applied by the main process (electron-main.js, TARGET_CSS_WIDTH / MIN_ZOOM) on
// every page load and Ctrl +/-/0, so the SPA neither reads nor sets it - it just lays out for ~1500 CSS px and up.

export interface PrinterInfo {
  name: string
  displayName: string
  isDefault: boolean
}

export interface StickerPrintJob {
  /** Complete HTML document for the label. */
  html: string
  /** Empty or undefined = the OS default printer. */
  printerName?: string
  copies: number
  widthMm: number
  heightMm: number
}

export interface PrintResult {
  success: boolean
  failureReason?: string
}

/** Shape of `window.nexpos` as exposed by electron-preload.js. Keep in step with that file. */
interface NexposBridge {
  isElectron: boolean
  listPrinters(): Promise<PrinterInfo[]>
  printSticker(job: StickerPrintJob): Promise<PrintResult>
}

declare global {
  interface Window {
    nexpos?: Partial<NexposBridge>
  }
}

export interface Electron {
  /** True inside the desktop shell. */
  readonly isElectron: boolean
  /** Printers installed on this computer; [] in a browser or if the shell can't list them. */
  listPrinters(): Promise<PrinterInfo[]>
  /** Silent sticker print. In a browser resolves `{ success: false, failureReason: 'unavailable' }` rather than throwing. */
  printSticker(job: StickerPrintJob): Promise<PrintResult>
}

// Read window.nexpos on every call rather than once at import, so tests (and a preload that lands late) work.
export const electron: Electron = {
  get isElectron() {
    return window.nexpos?.isElectron === true
  },
  async listPrinters() {
    const list = window.nexpos?.listPrinters
    if (!list) return []
    try {
      return await list()
    } catch {
      return []
    }
  },
  async printSticker(job) {
    const print = window.nexpos?.printSticker
    if (!print) return { success: false, failureReason: 'unavailable' }
    try {
      return await print(job)
    } catch (error) {
      return { success: false, failureReason: error instanceof Error ? error.message : 'error' }
    }
  },
}
