/**
 * Pure rules for what a cashier or a scanner typed into the scan box. Ported from POS.js (`parseQuantityPrefix`, `validateBarcodeInput`
 * and the Enter handler) so the same inputs do the same things.
 */

export const MAX_PREFIX_QUANTITY = 999
/** Barcodes up to Code 128 lengths are fine; anything longer is almost certainly a QR code. */
export const MAX_BARCODE_LENGTH = 50

export type ScanInput =
  | { kind: 'empty' }
  /** "5*" or "5x" alone: remember 5 for the next scan. */
  | { kind: 'quantity'; quantity: number }
  /** A code to look up, optionally with a quantity ("5*123456"). */
  | { kind: 'barcode'; barcode: string; quantity: number }

const QUANTITY_ONLY = /^(\d+)\s*[*xX]\s*$/
const QUANTITY_AND_CODE = /^(\d+)\s*[*xX]\s*(.+)$/

function validQuantity(n: number): boolean {
  return n > 0 && n <= MAX_PREFIX_QUANTITY
}

export function parseScanInput(raw: string): ScanInput {
  const input = raw.trim()
  if (!input) return { kind: 'empty' }

  const only = QUANTITY_ONLY.exec(input)
  if (only && validQuantity(Number(only[1]))) return { kind: 'quantity', quantity: Number(only[1]) }

  const both = QUANTITY_AND_CODE.exec(input)
  if (both && validQuantity(Number(both[1]))) return { kind: 'barcode', barcode: (both[2] ?? '').trim(), quantity: Number(both[1]) }

  return { kind: 'barcode', barcode: input, quantity: 1 }
}

/** True while the box holds the start of a quantity prefix ("5" or "5*"), which gets a highlight like the old `quantity-prefix-mode`. */
export function isTypingQuantityPrefix(value: string): boolean {
  return /^\d+[*xX]?$/.test(value)
}

export type RejectedBarcode = 'url' | 'contact' | 'structured' | 'too_long' | 'multiline' | 'special'

/**
 * A QR code is not a product barcode: the till refuses links, e-mail addresses, vCards, Wi-Fi and payment codes, JSON, very long
 * strings and anything with characters no linear barcode carries. Returns the reason, or null when the code may be looked up.
 */
export function rejectBarcode(input: string): RejectedBarcode | null {
  if (/^https?:\/\//i.test(input) || /^www\./i.test(input)) return 'url'
  if (/^(mailto|tel|sms):/i.test(input) || /^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$/.test(input)) return 'contact'
  if (/^(BEGIN:VCARD|MECARD:|WIFI:|geo:|bitcoin:|ethereum:|upi:)/i.test(input)) return 'structured'
  if (/^\s*[[{]/.test(input) && /[\]}]\s*$/.test(input)) {
    try {
      JSON.parse(input)
      return 'structured'
    } catch {
      // not JSON after all: keep checking
    }
  }
  if (input.length > MAX_BARCODE_LENGTH) return 'too_long'
  if (/[\r\n]/.test(input)) return 'multiline'
  if (/[<>{}|\\^~`[\]]/.test(input)) return 'special'
  return null
}
