import { useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { useSession } from '@/core/auth/useSession'
import { landingPath } from '@/shell/nav'
import { Button } from '@/ui/Button'
import { formatDate } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { StatusBadge } from '@/ui/StatusBadge'
import { useActivateLicense, useDeviceStatus, useLicenseDetails } from './api'
import styles from './license.module.css'
import { TillsSection } from './TillsSection'

const day = (value?: string | null) => (value ? formatDate(value) : '-')

/** `/app/license` - what is installed, this PC's Machine ID, and a box to paste a licence key into. */
export default function LicensePage() {
  const { t } = useTranslation('common')
  const session = useSession()
  const license = useLicenseDetails()
  // On a till PC the licence and Machine ID shown are the store server's (the till is only a browser): say so, and leave activating to the server.
  const isTill = useDeviceStatus().data?.isServer === false
  const activate = useActivateLicense()
  const [key, setKey] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [activated, setActivated] = useState(false)
  const [copied, setCopied] = useState(false)
  const machineIdRef = useRef<HTMLInputElement>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    setActivated(false)
    try {
      await activate.mutateAsync(key.trim())
      setKey('')
      setActivated(true)
    } catch (failure) {
      // A refused key lands on the field with the reason (incomplete, wrong computer, older than the active one ...).
      setError(isApiError(failure) ? (failure.errors?.licenseKey?.[0] ?? failure.message) : t('common.actionFailed'))
    }
  }

  const copy = async () => {
    const input = machineIdRef.current
    if (!input) return
    try {
      await navigator.clipboard.writeText(input.value)
    } catch {
      input.select()
      document.execCommand('copy')
    }
    setCopied(true)
    window.setTimeout(() => setCopied(false), 1500)
  }

  if (license.isPending) {
    return (
      <div className={styles.page}>
        <Loader size="md" tone="primary" />
      </div>
    )
  }

  if (license.isError) {
    return (
      <div className={styles.page}>
        <div className={styles.card}>
          <p className={styles.alert} data-tone="danger" role="alert">
            {license.error.message}
          </p>
          <div>
            <Button onClick={() => void license.refetch()}>{t('common.retry', 'Try again')}</Button>
          </div>
        </div>
      </div>
    )
  }

  const info = license.data
  const home = landingPath(session)

  return (
    <div className={styles.page}>
      <div className={styles.card}>
        <div className={styles.head}>
          <h2 className={styles.title}>{t('License_Title')}</h2>
          <StatusBadge tone={info.severity as 'success' | 'warning' | 'danger'} label={info.title} filled />
        </div>

        {activated && (
          <p className={styles.alert} data-tone="success" role="status">
            {t('License_Success')}
          </p>
        )}
        <p className={styles.alert} data-tone={info.severity} role="status">
          {info.message}
        </p>
        {isTill && (
          <p className={styles.hint} role="note">
            {t('License_TillNotice')}
          </p>
        )}

        {info.kind && (
          <dl className={styles.facts}>
            <dt>{t('License_Customer')}</dt>
            <dd>
              <bdi>{info.customer}</bdi>
            </dd>
            <dt>{t('License_Type')}</dt>
            <dd>{t(`License_Kind_${info.kind}`)}</dd>
            <dt>{t('License_Seats')}</dt>
            <dd>{info.seats}</dd>
            <dt>{t('License_Serial')}</dt>
            <dd className={styles.mono} dir="ltr">
              {info.serial}
            </dd>
            <dt>{t('License_IssuedOn')}</dt>
            <dd>
              <bdi>{day(info.issuedOn)}</bdi>
            </dd>
            <dt>{t(info.isTimeLimited ? 'License_ExpiresOn' : 'License_UpdatesUntil')}</dt>
            <dd>
              <bdi>{day(info.validUntil)}</bdi>
            </dd>
          </dl>
        )}

        <section className={styles.section}>
          <h2 className={styles.heading}>{t(isTill ? 'License_ServerMachineId' : 'License_MachineId')}</h2>
          <div className={styles.copyRow} dir="ltr">
            <input
              ref={machineIdRef}
              type="text"
              readOnly
              value={info.machineId}
              aria-label={t(isTill ? 'License_ServerMachineId' : 'License_MachineId')}
            />
            <Button onClick={() => void copy()}>{copied ? t('License_Copied') : t('License_Copy')}</Button>
          </div>
          <p className={styles.hint}>{t(isTill ? 'License_ServerMachineIdHelp' : 'License_MachineIdHelp')}</p>
        </section>

        {info.kind && info.seats > 1 && <TillsSection />}

        {!isTill && (
          <form className={styles.section} onSubmit={(event) => void submit(event)} noValidate>
            <h2 className={styles.heading}>{t('License_ActivateTitle')}</h2>
            <textarea
              value={key}
              onChange={(event) => setKey(event.target.value)}
              rows={4}
              dir="ltr"
              className={styles.mono}
              maxLength={4000}
              spellCheck={false}
              autoComplete="off"
              placeholder={t('License_PasteHint')}
              aria-label={t('License_ActivateTitle')}
              aria-invalid={error ? true : undefined}
            />
            {error && (
              <p className={styles.alert} data-tone="danger" role="alert">
                {error}
              </p>
            )}
            <div>
              <Button type="submit" variant="primary" loading={activate.isPending} disabled={key.trim().length === 0}>
                {t('License_ActivateButton')}
              </Button>
            </div>
          </form>
        )}

        {home && (
          <Link className={styles.back} to={home}>
            {t('License_BackToApp')}
          </Link>
        )}
      </div>
    </div>
  )
}
