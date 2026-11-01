import { formatNumber, ltrIsolate } from '@/ui/format'

const UNITS = ['B', 'KB', 'MB', 'GB', 'TB'] as const

/** `1.5 MB`: file sizes as people read them (1024 steps, like the Razor page), digits and unit kept together left-to-right. */
export function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return ltrIsolate('0 B')
  const exponent = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), UNITS.length - 1)
  const value = bytes / 1024 ** exponent
  return ltrIsolate(`${formatNumber(value, exponent === 0 ? 0 : 1)} ${UNITS[exponent]}`)
}
