import { useCallback, useEffect, useRef, useState } from 'react'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { queryKeys } from '@/api/queryClient'
import type { Session } from '@/api/types'
import { safeReturnTo } from '@/core/auth/returnTo'
import { sessionApi } from '@/core/auth/sessionApi'
import { IDLE_TIMEOUT_MS } from '@/core/idle/useIdleTimeout'
import { seedLanguage, setLanguage, useLanguage, type Language } from '@/core/i18n'
import { Loader } from '@/ui/Loader'
import { PinPad } from '@/ui/PinPad'
import { landingPath } from '../nav'
import styles from './LoginPage.module.css'

const PIN_LENGTH = 4

// Line icons for the three feature cards (boxes, people, chart): the same ideas as the Razor page's Font Awesome ones.
const FEATURES = [
  { key: 'Inventory', path: 'M3 10l9-5 9 5v8l-9 5-9-5z M3 10l9 5 9-5 M12 15v8' },
  { key: 'Customers', path: 'M9 11a3.5 3.5 0 100-7 3.5 3.5 0 000 7z M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6 M16 4.3a3.5 3.5 0 010 6.4 M18 14.3c2.3.7 3.5 2.6 3.5 5.7' },
  { key: 'Reports', path: 'M3.5 4v16h17 M7.5 15l3.5-4 3 2.5 5-6' },
] as const

const LANGUAGE_OPTIONS: { language: Language; label: string }[] = [
  { language: 'en', label: 'English' },
  { language: 'ar', label: 'العربية' },
]

/** English | العربية as the Razor page showed them: two pills, the current one filled. Writes the same `nexpos.lang` the rest of the app reads. */
function LanguageButtons({ className }: { className?: string }) {
  const { t } = useTranslation()
  const { language } = useLanguage()
  return (
    <div className={className} role="group" aria-label={t('Language')}>
      {LANGUAGE_OPTIONS.map((option) => (
        <button
          key={option.language}
          type="button"
          className={styles.language}
          data-active={option.language === language}
          aria-pressed={option.language === language}
          lang={option.language}
          onClick={() => void setLanguage(option.language)}
        >
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" aria-hidden="true">
            <circle cx="12" cy="12" r="9" />
            <path d="M3 12h18 M12 3c2.5 2.6 3.8 5.6 3.8 9s-1.3 6.4-3.8 9c-2.5-2.6-3.8-5.6-3.8-9s1.3-6.4 3.8-9z" />
          </svg>
          {option.label}
        </button>
      ))}
    </div>
  )
}

/**
 * PIN sign-in, the same two steps as the Razor page: pick your name (skipped when this PC remembers who signed in last),
 * then a four-digit pad that submits by itself. A 401 here is a wrong PIN, never an expired session.
 */
export default function LoginPage() {
  const { t } = useTranslation(['account', 'common'])
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const returnTo = safeReturnTo(params.get('returnTo'))
  const idle = params.get('reason') === 'idle'

  // Already signed in (e.g. through the Razor page - same cookie): skip the form.
  const existing = useQuery({ queryKey: queryKeys.session, queryFn: ({ signal }) => sessionApi.get(signal), retry: false, staleTime: 0 })
  const options = useQuery({ queryKey: queryKeys.signInOptions, queryFn: sessionApi.signInOptions, staleTime: 0 })

  // `undefined` = follow the server's remembered user; `null` = the person asked to pick someone else.
  const [chosen, setChosen] = useState<string | null | undefined>(undefined)
  const [pin, setPin] = useState('')
  const [shake, setShake] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  const users = options.data?.users ?? []
  const username = chosen === undefined ? options.data?.lastUsername || null : chosen
  const person = users.find((u) => u.username === username)
  const branding = options.data?.branding

  const login = useMutation({
    mutationFn: (digits: string) => sessionApi.login({ username: username ?? '', pin: digits, rememberMe: false }),
    onSuccess: async (session: Session) => {
      queryClient.setQueryData(queryKeys.session, session)
      await seedLanguage(session.language)
      void navigate(returnTo ?? landingPath(session) ?? '/', { replace: true })
    },
    onError: (error) => {
      setMessage(isApiError(error) ? error.message : t('Error', { ns: 'common' }))
      setShake(true)
      setPin('')
    },
  })

  const onKey = useCallback(
    (key: string) => {
      if (login.isPending) return
      setMessage(null)
      if (key === 'C') setPin('')
      else if (key === '⌫') setPin((p) => p.slice(0, -1))
      else if (key >= '0' && key <= '9') setPin((p) => (p.length < PIN_LENGTH ? p + key : p))
    },
    [login.isPending],
  )

  // Submit as soon as the fourth digit lands (once per PIN).
  const submitted = useRef('')
  useEffect(() => {
    if (pin.length === PIN_LENGTH && username && submitted.current !== pin) {
      submitted.current = pin
      login.mutate(pin)
    }
    if (pin.length === 0) submitted.current = ''
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `login` is a fresh object each render; only the PIN should trigger a submit
  }, [pin, username])

  useEffect(() => {
    if (!shake) return
    const timer = window.setTimeout(() => setShake(false), 400)
    return () => window.clearTimeout(timer)
  }, [shake])

  if (existing.data) return <Navigate to={returnTo ?? landingPath(existing.data) ?? '/'} replace />

  const pick = (name: string) => {
    setChosen(name)
    setPin('')
    setMessage(null)
  }

  return (
    <div className={styles.page}>
      <div className={styles.shapes} aria-hidden="true">
        <span className={styles.shape} />
        <span className={styles.shape} />
        <span className={styles.shape} />
        <span className={styles.shape} />
      </div>

      <aside className={styles.brand}>
        {branding &&
          (branding.hasCustomLogo ? (
            // An uploaded logo can be any shape (often a wide wordmark): show it as is, with no tile behind it.
            <img className={styles.logoWide} src={branding.logoUrl} alt="" />
          ) : (
            <span className={styles.logoTile}>
              <img className={styles.logoMark} src={branding.markUrl} alt="" />
            </span>
          ))}
        <h1 className={styles.appName}>{branding?.appName ?? 'NexPOS'}</h1>
        <p className={styles.tagline}>{t('PointOfSaleSystem')}</p>
        <p className={styles.description}>{t('SystemDescription')}</p>

        <div className={styles.features}>
          {FEATURES.map((feature) => (
            <div key={feature.key} className={styles.feature}>
              <svg className={styles.featureIcon} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d={feature.path} />
              </svg>
              <div className={styles.featureText}>{t(feature.key)}</div>
            </div>
          ))}
        </div>

        <LanguageButtons className={styles.languages} />
      </aside>

      <main className={styles.formSide}>
        <div className={styles.formWrap}>
          <div className={styles.card}>
            {idle && <div className={styles.notice}>{t('Shell_SignedOutIdle', { 0: IDLE_TIMEOUT_MS / 60_000, ns: 'common' })}</div>}

            {options.isPending || existing.isPending ? (
              <Loader fullSection />
            ) : options.isError ? (
              <div className={styles.error} role="alert">
                {options.error.message}
              </div>
            ) : person ? (
              <section className={styles.step} aria-live="polite">
                <div className={styles.avatar}>{person.initials}</div>
                <div className={styles.welcome}>{t('WelcomeBack')}</div>
                <div className={styles.name}>
                  <bdi>{person.displayName}</bdi>
                </div>

                <PinPad pin={pin} onKey={onKey} dotCount={PIN_LENGTH} shake={shake} />

                <div className={styles.status} role="status">
                  {login.isPending ? <Loader size="sm" label={t('Shell_SigningIn', { ns: 'common' })} /> : message && <span className={styles.errorText}>{message}</span>}
                </div>

                {users.length > 1 && (
                  <button type="button" className={styles.link} onClick={() => setChosen(null)}>
                    {t('NotYouSwitchUser')}
                  </button>
                )}
              </section>
            ) : (
              <section className={styles.step}>
                <h2 className={styles.pickTitle}>{t('WhoIsSigningIn')}</h2>
                {users.length === 0 ? (
                  <div className={styles.empty}>{t('NoUsersToSignIn')}</div>
                ) : (
                  <div className={styles.people}>
                    {users.map((u) => (
                      <button key={u.username} type="button" className={styles.person} onClick={() => pick(u.username)}>
                        <span className={styles.initials}>{u.initials}</span>
                        <span className={styles.personName}>
                          <bdi>{u.displayName}</bdi>
                        </span>
                      </button>
                    ))}
                  </div>
                )}
                {chosen === null && options.data?.lastUsername && (
                  <button type="button" className={styles.link} onClick={() => setChosen(undefined)}>
                    {t('Cancel')}
                  </button>
                )}
              </section>
            )}
          </div>

          <small className={styles.footer}>
            © {new Date().getFullYear()} {branding?.appName ?? 'NexPOS'}. {t('Copyright')}
          </small>

          <LanguageButtons className={styles.mobileLanguages} />
        </div>
      </main>
    </div>

  )
}
