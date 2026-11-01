/**
 * Tax periods are whole days. The date pickers give `yyyy-mm-dd`; the API stores instants, so a period starts at 00:00 UTC of its first day and
 * ends at the last second of its last day (the Razor page sent the bare date, which made "until 31 Dec" stop at the start of 31 Dec).
 */
export const startOfDay = (day: string): string | null => (day ? `${day}T00:00:00Z` : null)

export const endOfDay = (day: string): string | null => (day ? `${day}T23:59:59Z` : null)

/** The `yyyy-mm-dd` an instant belongs to, read in UTC (the same way {@link startOfDay} wrote it); `''` for none. */
export const dayOf = (instant: string | null | undefined): string => (instant ? instant.slice(0, 10) : '')
