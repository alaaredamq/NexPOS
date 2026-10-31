import { useTranslation } from 'react-i18next'
import { DEFAULT_LANGUAGE, directionOf, isLanguage, type Direction, type Language } from './languages'

/** Current language + direction, re-rendering when it changes. `setLanguage` lives in `core/i18n` (index). */
export function useLanguage(): { language: Language; dir: Direction } {
  const { i18n } = useTranslation()
  const language = isLanguage(i18n.resolvedLanguage) ? i18n.resolvedLanguage : DEFAULT_LANGUAGE
  return { language, dir: directionOf(language) }
}
