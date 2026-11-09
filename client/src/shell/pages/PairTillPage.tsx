import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { useDeviceStatus, usePairTill } from '@/modules/license/api'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import styles from './PairTillPage.module.css'

/**
 * `/app/pair-till` (public). A till PC - any computer other than the store server - opens this once and types the code the
 * administrator got from Settings > License. The server answers with a device cookie; after that the PC can sell. The store server's own
 * PC never needs this and is told so.
 */
export default function PairTillPage() {
  const { t } = useTranslation('common')
  const status = useDeviceStatus()
  const pair = usePairTill()
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [pairedAs, setPairedAs] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    try {
      setPairedAs((await pair.mutateAsync(code.trim())).name)
    } catch (failure) {
      setError(isApiError(failure) ? failure.message : t('common.actionFailed'))
    }
  }

  let body
  if (status.isPending) {
    body = <Loader size="md" tone="primary" />
  } else if (pairedAs) {
    body = (
      <>
        <p className={styles.ok} role="status">
          {t('PairTill_Done', { name: pairedAs })}
        </p>
        <Link to="/">
          <Button variant="primary">{t('PairTill_Continue')}</Button>
        </Link>
      </>
    )
  } else if (status.data?.isServer || status.data?.access === 'Allowed') {
    body = (
      <>
        <p className={styles.body}>{status.data.isServer ? t('PairTill_IsServer') : t('PairTill_AlreadyPaired')}</p>
        <Link to="/">
          <Button variant="primary">{t('PairTill_Continue')}</Button>
        </Link>
      </>
    )
  } else {
    body = (
      <form onSubmit={(event) => void submit(event)} noValidate>
        <p className={styles.body}>{t(status.data?.access === 'SeatsExceeded' ? 'License_SeatsExceeded' : 'PairTill_Body')}</p>
        <p>
          <input
            className={styles.code}
            value={code}
            onChange={(event) => setCode(event.target.value)}
            maxLength={12}
            dir="ltr"
            autoComplete="off"
            spellCheck={false}
            placeholder="XXXX-XXXX"
            aria-label={t('PairTill_CodeLabel')}
            aria-invalid={error ? true : undefined}
          />
        </p>
        {error && (
          <p className={styles.alert} role="alert">
            {error}
          </p>
        )}
        <Button type="submit" variant="primary" loading={pair.isPending} disabled={code.trim().length === 0}>
          {t('PairTill_Button')}
        </Button>
      </form>
    )
  }

  return (
    <div className={styles.page}>
      <div className={styles.card}>
        <h1 className={styles.title}>{t('PairTill_Title')}</h1>
        {body}
      </div>
    </div>
  )
}
