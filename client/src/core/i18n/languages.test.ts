import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { LANGUAGE_COOKIE, LANGUAGE_STORAGE_KEY, readStoredLanguage, syncLanguageFromCookie, writeStoredLanguage } from './languages'

const clearCookie = () => {
  document.cookie = `${LANGUAGE_COOKIE}=; path=/; max-age=0`
}

beforeEach(() => {
  localStorage.clear()
  clearCookie()
})
afterEach(clearCookie)

describe('language shared with the old Razor pages', () => {
  it('takes the language a Razor page set in the cookie, even over the one stored earlier', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en')
    document.cookie = `${LANGUAGE_COOKIE}=ar; path=/`

    syncLanguageFromCookie()

    expect(readStoredLanguage()).toBe('ar')
  })

  it('leaves the stored language alone when there is no cookie (first visit, or cookies blocked)', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'ar')

    syncLanguageFromCookie()

    expect(readStoredLanguage()).toBe('ar')
    expect(document.cookie).not.toContain(LANGUAGE_COOKIE)
  })

  it('ignores a cookie that is not a language we have', () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, 'en')
    document.cookie = `${LANGUAGE_COOKIE}=fr; path=/`

    syncLanguageFromCookie()

    expect(readStoredLanguage()).toBe('en')
  })

  it('writes the cookie whenever the language is chosen, so a Razor page opened next can follow', () => {
    writeStoredLanguage('ar')

    expect(document.cookie).toContain(`${LANGUAGE_COOKIE}=ar`)
    expect(localStorage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar')
  })
})
