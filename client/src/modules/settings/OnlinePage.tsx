import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { formatDate, formatDateTime } from '@/ui/format'
import { Loader } from '@/ui/Loader'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { useConnectOnline, useDisconnectOnline, useOnlineStatus, useSyncOnlineNow, type OnlineStatus } from './api'
import { PrivacyCard } from './SealingFlow'
import { SettingsLayout } from './SettingsLayout'
import styles from './settings.module.css'

/** Codes the connect call can fail with; each has a plain sentence in the `settings` strings (`Online_Connect_<code>`). */
const CONNECT_CODES = [
  'enroll_link_invalid',
  'cloud_insecure_url',
  'enroll_code_invalid',
  'enroll_code_expired',
  'enroll_code_used',
  'site_disabled',
  'rate_limited',
  'cloud_unreachable',
  'cloud_error',
] as const

/** Why the last sync did not go through (`Online_Problem_<key>`). Anything unlisted falls back to "will retry". */
const PROBLEM_KEYS: Record<string, string> = {
  network: 'network',
  timeout: 'network',
  subscription_inactive: 'subscription',
  unauthorized: 'unauthorized',
  schema_newer: 'schemaNewer',
  seal_key: 'sealKey',
  seal_key_missing: 'sealKeyMissing',
}

/** `/app/settings/online` - connect this till to NexPOS Online with the provider's link, and see how it is doing. */
export default function OnlinePage() {
  const status = useOnlineStatus()

  return (
    <SettingsLayout>
      {status.isPending && <Loader fullSection />}
      {status.isError && <p className={styles.error}>{status.error.message}</p>}
      {status.data && (status.data.connected ? <ConnectedCard status={status.data} /> : <ConnectCard status={status.data} />)}
    </SettingsLayout>
  )
}

function ConnectCard({ status }: { status: OnlineStatus }) {
  const { t } = useTranslation('settings')
  const connect = useConnectOnline()
  const [link, setLink] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    if (!link.trim()) {
      setError(t('Online_Connect_enroll_link_invalid'))
      return
    }
    try {
      await connect.mutateAsync(link.trim())
      setLink('')
      toast.success(t('Online_Connected'))
    } catch (failure) {
      const code = isApiError(failure) ? failure.code : ''
      setError((CONNECT_CODES as readonly string[]).includes(code) ? t(`Online_Connect_${code}`) : isApiError(failure) ? failure.message : t('Online_Connect_cloud_error'))
    }
  }

  return (
    <form className={styles.card} onSubmit={(event) => void submit(event)} noValidate>
      <h2 className={styles.cardTitle}>{t('Online_Title')}</h2>
      <p className={styles.cardHelp}>{t('Online_Intro')}</p>

      {status.settingsProblem && (
        <p className={styles.alert} data-tone="warning" role="alert">
          {t('Online_SettingsProblem')}
        </p>
      )}

      <ol className={styles.steps}>
        <li>{t('Online_Step1')}</li>
        <li>{t('Online_Step2')}</li>
        <li>{t('Online_Step3')}</li>
      </ol>
      <p className={styles.note}>{t('Online_Seal_ConnectNote')}</p>

      <label className={styles.fieldLabel} htmlFor="online-link">
        {t('Online_LinkLabel')}
      </label>
      <textarea
        id="online-link"
        className={styles.linkBox}
        value={link}
        onChange={(event) => setLink(event.target.value)}
        rows={3}
        dir="ltr"
        spellCheck={false}
        autoComplete="off"
        maxLength={4000}
        placeholder="https://…/enroll/XXXX-XXXX-XXXX"
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? 'online-link-error' : undefined}
      />
      {error && (
        <p id="online-link-error" className={styles.alert} data-tone="danger" role="alert">
          {error}
        </p>
      )}

      <div className={styles.actions}>
        <Button type="submit" variant="primary" loading={connect.isPending}>
          {t('Online_Connect')}
        </Button>
      </div>
      <p className={styles.note}>{t('Online_OfflineNote')}</p>
    </form>
  )
}

function ConnectedCard({ status }: { status: OnlineStatus }) {
  const { t } = useTranslation('settings')
  const sync = useSyncOnlineNow()
  const disconnect = useDisconnectOnline()

  const problemKey = status.lastErrorCode ? (PROBLEM_KEYS[status.lastErrorCode] ?? 'other') : null
  const plan = status.subscriptionStatus
  const planTone = plan === 'Active' ? 'success' : plan === 'Grace' ? 'warning' : plan ? 'danger' : 'neutral'
  const expires = status.subscriptionExpiresAtUtc ? formatDate(status.subscriptionExpiresAtUtc) : null

  const leave = async () => {
    const ok = await confirm({
      title: t('Online_DisconnectTitle'),
      message: t('Online_DisconnectMessage'),
      confirmLabel: t('Online_Disconnect'),
      danger: true,
    })
    if (!ok) return
    try {
      await disconnect.mutateAsync()
      toast.success(t('Online_Disconnected'))
    } catch (failure) {
      toast.error(isApiError(failure) ? failure.message : t('Online_Connect_cloud_error'))
    }
  }

  return (
    <div className={styles.stack}>
      <section className={styles.card} aria-label={t('Online_Title')}>
        <div className={styles.cardHead}>
          <h2 className={styles.cardTitle}>{t('Online_Title')}</h2>
          <StatusBadge tone={problemKey ? 'warning' : 'success'} label={problemKey ? t('Online_StatusAttention') : t('Online_StatusConnected')} filled />
        </div>

        {problemKey && (
          <p className={styles.alert} data-tone={problemKey === 'network' ? 'warning' : 'danger'} role="alert">
            {t(`Online_Problem_${problemKey}`)}
          </p>
        )}

        <dl className={styles.facts}>
          <dt>{t('Online_Shop')}</dt>
          <dd>
            <bdi>{status.clientName ?? '-'}</bdi>
            {status.siteName ? (
              <>
                {' · '}
                <bdi>{status.siteName}</bdi>
              </>
            ) : null}
          </dd>

          <dt>{t('Online_LastSync')}</dt>
          <dd>{status.lastSuccessUtc ? <bdi>{formatDateTime(status.lastSuccessUtc)}</bdi> : t('Online_NeverSynced')}</dd>

          <dt>{t('Online_Waiting')}</dt>
          <dd>{status.pendingChanges > 0 ? t('Online_WaitingCount', { n: status.pendingChanges }) : t('Online_AllSent')}</dd>

          <dt>{t('Online_Plan')}</dt>
          <dd>
            {plan ? (
              <>
                <StatusBadge tone={planTone} label={t(`Online_Plan_${plan}`)} />
                {expires ? (
                  <>
                    {' '}
                    <span className={styles.muted}>
                      {t(plan === 'Active' ? 'Online_PlanUntil' : 'Online_PlanEnded', { date: expires })}
                    </span>
                  </>
                ) : null}
              </>
            ) : (
              <span className={styles.muted}>{t('Online_PlanUnknown')}</span>
            )}
          </dd>

          <dt>{t('Online_Server')}</dt>
          <dd dir="ltr" className={styles.mono}>
            {status.cloudHost ?? '-'}
          </dd>
        </dl>

        <div className={styles.actions}>
          <Button variant="secondary" loading={sync.isPending} onClick={() => sync.mutate()}>
            {t('Online_SyncNow')}
          </Button>
          <Button variant="danger" loading={disconnect.isPending} onClick={() => void leave()}>
            {t('Online_Disconnect')}
          </Button>
        </div>
        <p className={styles.note}>{t('Online_OfflineNote')}</p>
      </section>

      <PrivacyCard status={status} />
    </div>
  )
}
