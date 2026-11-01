/**
 * Tells a barcode scanner from a person typing. A scanner "types" its digits at machine speed (typically a few ms apart, always well
 * under 50) and ends with Enter; a person is never that regular. The product search box uses this so a scan that lands in it (because
 * focus drifted there) is treated as a barcode instead of a name search.
 */

/** Longest average gap between keystrokes that still counts as a scanner. */
export const SCANNER_MAX_AVERAGE_GAP_MS = 35
/** Shortest burst worth calling a scan; a few quick keys from a fast typist are not. */
export const SCANNER_MIN_LENGTH = 6

export class KeystrokeTimer {
  private times: number[] = []

  /** Call once per character typed. */
  record(now: number = performance.now()): void {
    this.times.push(now)
  }

  reset(): void {
    this.times = []
  }

  /** The burst so far came in faster than a person can type. */
  looksLikeScan(minLength: number = SCANNER_MIN_LENGTH): boolean {
    if (this.times.length < minLength) return false
    const span = (this.times.at(-1) ?? 0) - (this.times[0] ?? 0)
    return span / (this.times.length - 1) <= SCANNER_MAX_AVERAGE_GAP_MS
  }
}
