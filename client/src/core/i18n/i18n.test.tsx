import { act, render, screen } from '@testing-library/react'
import { Suspense } from 'react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { I18nextProvider, useTranslation } from 'react-i18next'
import { i18n, seedLanguage, setLanguage, loadNamespaces } from './index'
import { formatDate, formatFixed, formatNumber, getLanguage, LANGUAGE_STORAGE_KEY, useLanguage } from './index'

function Probe() {
  const { t } = useTranslation('orders')
  const { language, dir } = useLanguage()
  return (
    <p data-testid="probe" data-lang={language} data-dir={dir}>
      {t('OrdersManagement')}
    </p>
  )
}

const renderProbe = () =>
  render(
    <I18nextProvider i18n={i18n}>
      <Suspense fallback="loading">
        <Probe />
      </Suspense>
    </I18nextProvider>,
  )

beforeEach(async () => {
  localStorage.clear()
  await setLanguage('en')
  localStorage.clear()
})
afterEach(() => localStorage.clear())

describe('language state', () => {
  it('defaults to English and ignores garbage in storage', () => {
    expect(getLanguage()).toBe('en')
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'fr')
    expect(getLanguage()).toBe('en')
  })

  it('setLanguage persists to nexpos.lang and flips <html lang dir>', async () => {
    await setLanguage('ar')
    expect(localStorage.getItem('nexpos.lang')).toBe('ar')
    expect(document.documentElement.lang).toBe('ar')
    expect(document.documentElement.dir).toBe('rtl')
    await setLanguage('en')
    expect(document.documentElement.lang).toBe('en')
    expect(document.documentElement.dir).toBe('ltr')
  })

  it('seedLanguage adopts the server language only when nothing is stored', async () => {
    await seedLanguage('ar')
    expect(getLanguage()).toBe('ar')
    await seedLanguage('en') // a stored choice wins
    expect(getLanguage()).toBe('ar')
    await seedLanguage('fr') // unsupported: ignored
    expect(getLanguage()).toBe('ar')
  })
})

describe('translations', () => {
  it('lazy-loads a namespace and switches EN ⇄ AR without a reload', async () => {
    await loadNamespaces('orders')
    renderProbe()
    expect(await screen.findByTestId('probe')).toHaveTextContent('Orders Management')
    expect(screen.getByTestId('probe')).toHaveAttribute('data-dir', 'ltr')

    await act(() => setLanguage('ar'))
    const probe = await screen.findByTestId('probe')
    expect(probe).toHaveAttribute('data-lang', 'ar')
    expect(probe).toHaveAttribute('data-dir', 'rtl')
    expect(probe.textContent).not.toBe('Orders Management')
    expect(probe.textContent).toMatch(/[؀-ۿ]/)
  })

  it('falls back to English for a key missing in Arabic, and to the key itself when unknown', async () => {
    await setLanguage('ar')
    i18n.addResource('en', 'common', 'only.en', 'English only')
    expect(i18n.t('only.en')).toBe('English only')
    expect(i18n.t('No such key.')).toBe('No such key.')
  })

  it('keeps keys with dots/colons intact and fills {0} / {name} placeholders', async () => {
    i18n.addResources('en', 'common', { 'Signing in...': 'Signing in…', 'Pack of {0}': 'Pack of {{0}}', 'Hi {name}: ok': 'Hi {{name}}: ok' })
    expect(i18n.t('Signing in...')).toBe('Signing in…')
    expect(i18n.t('Pack of {0}', { 0: 12 })).toBe('Pack of 12')
    expect(i18n.t('Hi {name}: ok', { name: 'Sam' })).toBe('Hi Sam: ok')
  })
})

  it('serves the common.* keys that ui/ components ask for, with {{x}} interpolation, in both languages', async () => {
    await setLanguage('en')
    expect(i18n.t('common.pageRange', { from: 1, to: 10, total: 50 })).toBe('1–10 of 50')
    await setLanguage('ar')
    expect(i18n.t('common.pageRange', { from: 1, to: 10, total: 50 })).toBe('1–10 من 50')
    expect(i18n.t('common.close')).toBe('إغلاق')
  })

describe('number and date formatting', () => {
  it('uses Western digits in both languages', () => {
    expect(formatFixed(1234.5, 2, 'en')).toBe('1,234.50')
    expect(formatFixed(1234.5, 2, 'ar')).toBe('1,234.50')
    expect(formatNumber(9876543210, undefined, 'ar')).toMatch(/^[0-9,]+$/)
  })

  it('formats dates with Arabic text but Western digits', () => {
    const date = new Date(Date.UTC(2026, 9, 31, 12))
    const ar = formatDate(date, { dateStyle: 'long', timeZone: 'UTC' }, 'ar')
    expect(ar).toMatch(/2026/)
    expect(ar).not.toMatch(/[٠-٩]/)
    expect(formatDate(date, { dateStyle: 'long', timeZone: 'UTC' }, 'en')).toBe('October 31, 2026')
  })
})
