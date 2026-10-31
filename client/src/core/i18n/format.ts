// Number and date formatting. Decision (mirrors the current app): digits are ALWAYS Western (0-9), also in
// Arabic. Every money/quantity formatter in the old JS uses 'en-US', and the .NET side (receipts, PDFs) never
// substitutes digits, so Eastern Arabic digits would make screen and printed receipt disagree.
// Only the surrounding text (month names, weekday names, separators' position) follows the language.
//
// `-u-nu-latn` forces Latin digits for the `ar` locale, whose default numbering system is Arabic-Indic.

import { getLanguage, type Language } from './languages'

const INTL_LOCALE: Record<Language, string> = {
  en: 'en-US',
  ar: 'ar-u-nu-latn',
}

/** Locale tag to hand to `Intl.*`/`toLocaleString`. Use this, never a literal 'en-US'/'ar'. */
export function intlLocale(language: Language = getLanguage()): string {
  return INTL_LOCALE[language]
}

export function formatNumber(value: number, options?: Intl.NumberFormatOptions, language?: Language): string {
  return new Intl.NumberFormat(intlLocale(language), options).format(value)
}

/** Fixed decimals — the old `toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })`. */
export function formatFixed(value: number, decimals = 2, language?: Language): string {
  return formatNumber(value, { minimumFractionDigits: decimals, maximumFractionDigits: decimals }, language)
}

type DateInput = Date | string | number

export function formatDate(
  value: DateInput,
  options: Intl.DateTimeFormatOptions = { dateStyle: 'medium' },
  language?: Language,
): string {
  return new Intl.DateTimeFormat(intlLocale(language), options).format(new Date(value))
}

export function formatDateTime(value: DateInput, language?: Language): string {
  return formatDate(value, { dateStyle: 'medium', timeStyle: 'short' }, language)
}
