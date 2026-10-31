import { useTranslation } from 'react-i18next'
import { setLanguage, useLanguage, type Language } from '@/core/i18n'
import { useRefreshSession } from '@/core/auth/useSession'
import styles from './LanguageToggle.module.css'

const OPTIONS: { language: Language; label: string }[] = [
  { language: 'en', label: 'EN' },
  { language: 'ar', label: 'عربي' },
]

/** EN | AR. Persists to `localStorage['nexpos.lang']`, flips `<html lang dir>`, and re-fetches the session because nav labels are localized by the server. */
export function LanguageToggle({ signedIn = true }: { signedIn?: boolean }) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  const refreshSession = useRefreshSession()

  const choose = async (next: Language) => {
    if (next === language) return
    await setLanguage(next)
    if (signedIn) await refreshSession()
  }

  return (
    <div className={styles.switcher} role="group" aria-label={t('Language')}>
      {OPTIONS.map((option) => (
        <button
          key={option.language}
          type="button"
          className={styles.option}
          data-active={option.language === language}
          aria-pressed={option.language === language}
          lang={option.language}
          onClick={() => void choose(option.language)}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}
