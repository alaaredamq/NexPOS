import { useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { ManagerPinModal } from '@/ui/ManagerPinModal'
import { Modal } from '@/ui/Modal'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import {
  downloadRecoverySlipPdf,
  getRecoverySlip,
  printRecoverySlip,
  useConfirmSealing,
  useRestoreSealKey,
  type OnlineStatus,
  type RecoverySlip,
} from './api'
import styles from './settings.module.css'

type SealingState = 'off' | 'needsSlip' | 'keyMissing' | 'on'

/**
 * Private Online (docs/online/PRIVACY-KEYS.md): the shop's data is locked on this PC before it is sent, with a key only the shop
 * holds. This card walks the owner through the one step that matters: keeping the recovery code, so a lost PC does not mean a
 * lost cloud copy. What it shows follows `sealingState`, which the server decides.
 */
export function PrivacyCard({ status }: { status: OnlineStatus }) {
  const { t } = useTranslation('settings')
  const state = (status.sealingState ?? 'off') as SealingState
  const [dialog, setDialog] = useState<'slip' | 'view' | 'restore' | null>(null)

  const makeNewKey = async () => {
    const ok = await confirm({
      title: t('Online_Seal_NewKeyTitle'),
      message: t('Online_Seal_NewKeyMessage'),
      confirmLabel: t('Online_Seal_NewKey'),
      danger: true,
    })
    if (ok) setDialog('slip')
  }

  return (
    <>
      <section className={styles.card} aria-label={t('Online_Seal_Title')}>
        <div className={styles.cardHead}>
          <h2 className={styles.cardTitle}>{t('Online_Seal_Title')}</h2>
          {state === 'on' && <StatusBadge tone="success" label={t('Online_Seal_Locked')} filled />}
          {state === 'needsSlip' && <StatusBadge tone="warning" label={t('Online_Seal_NotUploading')} filled />}
          {state === 'keyMissing' && <StatusBadge tone="danger" label={t('Online_Seal_NotUploading')} filled />}
        </div>

        {state === 'on' && (
          <>
            <p className={styles.cardHelp}>{t('Online_Seal_OnHelp')}</p>
            {status.sealKeyId && (
              <dl className={styles.facts}>
                <dt>{t('Online_Seal_KeyId')}</dt>
                <dd dir="ltr" className={styles.mono}>
                  {status.sealKeyId}
                </dd>
              </dl>
            )}
            <div className={styles.actions}>
              <Button variant="secondary" onClick={() => setDialog('view')}>
                {t('Online_Seal_ShowCode')}
              </Button>
            </div>
          </>
        )}

        {state === 'off' && (
          <>
            <p className={styles.cardHelp}>{t('Online_Seal_OffHelp')}</p>
            <div className={styles.actions}>
              <Button variant="primary" onClick={() => setDialog('slip')}>
                {t('Online_Seal_TurnOn')}
              </Button>
            </div>
          </>
        )}

        {state === 'needsSlip' && (
          <>
            <p className={styles.cardHelp}>{t('Online_Seal_NeedsSlipHelp')}</p>
            <div className={styles.actions}>
              <Button variant="secondary" onClick={() => setDialog('restore')}>
                {t('Online_Seal_HaveCode')}
              </Button>
              <Button variant="primary" onClick={() => setDialog('slip')}>
                {t('Online_Seal_ShowCode')}
              </Button>
            </div>
          </>
        )}

        {state === 'keyMissing' && (
          <>
            <p className={styles.cardHelp}>{t('Online_Seal_KeyMissingHelp')}</p>
            <div className={styles.actions}>
              <Button variant="danger" onClick={() => void makeNewKey()}>
                {t('Online_Seal_NewKey')}
              </Button>
              <Button variant="primary" onClick={() => setDialog('restore')}>
                {t('Online_Seal_EnterCode')}
              </Button>
            </div>
          </>
        )}
      </section>

      {(dialog === 'slip' || dialog === 'view') && <RecoverySlipFlow mode={dialog === 'slip' ? 'enable' : 'view'} onClose={() => setDialog(null)} />}
      {dialog === 'restore' && <RestoreKeyDialog onClose={() => setDialog(null)} />}
    </>
  )
}

/**
 * PIN first (the server checks it with the request that reveals the code), then the code itself with print and PDF, and in `enable`
 * mode the owner's confirmation that they kept it: only then does the shop start uploading, locked.
 */
function RecoverySlipFlow({ mode, onClose }: { mode: 'enable' | 'view'; onClose: () => void }) {
  const { t } = useTranslation('settings')
  const confirmSealing = useConfirmSealing()
  const [slip, setSlip] = useState<RecoverySlip | null>(null)
  const [kept, setKept] = useState(false)
  const [busy, setBusy] = useState<'print' | 'pdf' | null>(null)
  // The PIN stays in memory for this dialog only: print and PDF ask the server again, and the server checks it again each time.
  const pinRef = useRef('')

  const verify = async (pin: string) => {
    try {
      const result = await getRecoverySlip(pin)
      pinRef.current = pin
      setSlip(result)
      return true
    } catch (failure) {
      // A wrong PIN just shakes the pad. Being locked out needs a sentence.
      if (isApiError(failure) && failure.code === 'pin_locked') toast.error(failure.message)
      return false
    }
  }

  if (!slip) {
    return <ManagerPinModal action={t('Online_Seal_PinAction')} verify={verify} onSuccess={() => undefined} onCancel={onClose} />
  }

  const output = async (kind: 'print' | 'pdf') => {
    setBusy(kind)
    try {
      if (kind === 'pdf') {
        await downloadRecoverySlipPdf(pinRef.current)
      } else {
        const result = await printRecoverySlip(pinRef.current)
        if (result.printed) toast.success(result.message)
        else toast.error(result.message)
      }
    } catch (failure) {
      toast.error(isApiError(failure) ? failure.message : t('Online_Connect_cloud_error'))
    } finally {
      setBusy(null)
    }
  }

  const start = async () => {
    try {
      await confirmSealing.mutateAsync()
      toast.success(t('Online_Seal_Started'))
      onClose()
    } catch (failure) {
      toast.error(isApiError(failure) ? failure.message : t('Online_Connect_cloud_error'))
    }
  }

  return (
    <Modal onClose={onClose} width={560} title={t('Online_Seal_SlipTitle')}>
      <div className={styles.stack}>
        <p className={styles.cardHelp}>{t('Online_Seal_SlipHelp')}</p>

        <div className={styles.codeGrid} dir="ltr" role="group" aria-label={t('Online_Seal_SlipTitle')}>
          {slip.groups.map((group, index) => (
            <span key={index} className={styles.codeGroup}>
              {group}
            </span>
          ))}
        </div>
        <p className={styles.note}>
          {t('Online_Seal_KeyId')}: <bdi className={styles.mono}>{slip.keyId}</bdi>
        </p>

        <div className={styles.actions}>
          <Button variant="primary" loading={busy === 'print'} disabled={busy === 'pdf'} onClick={() => void output('print')}>
            {t('Print_ReceiptPaper', { ns: 'common' })}
          </Button>
          <Button loading={busy === 'pdf'} disabled={busy === 'print'} onClick={() => void output('pdf')}>
            {t('Print_DownloadPdf', { ns: 'common' })}
          </Button>
        </div>

        {mode === 'enable' ? (
          <>
            <label className={styles.checkRow}>
              <input type="checkbox" checked={kept} onChange={(event) => setKept(event.target.checked)} />
              <span>{t('Online_Seal_Kept')}</span>
            </label>
            <p className={styles.note}>{t('Online_Seal_ResendNote')}</p>
            <div className={styles.actions}>
              <Button variant="secondary" onClick={onClose}>
                {t('Online_Seal_NotNow')}
              </Button>
              <Button variant="primary" disabled={!kept} loading={confirmSealing.isPending} onClick={() => void start()}>
                {t('Online_Seal_Confirm')}
              </Button>
            </div>
          </>
        ) : (
          <div className={styles.actions}>
            <Button variant="primary" onClick={onClose}>
              {t('Online_Seal_Done')}
            </Button>
          </div>
        )}
      </div>
    </Modal>
  )
}

/** A restore on a new PC: the code from the slip and the user's PIN. A wrong PIN and a wrong code each say so in their own words. */
function RestoreKeyDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('settings')
  const restore = useRestoreSealKey()
  const [code, setCode] = useState('')
  const [pin, setPin] = useState('')
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    try {
      await restore.mutateAsync({ pin, code })
      toast.success(t('Online_Seal_Restored'))
      onClose()
    } catch (failure) {
      if (isApiError(failure)) setError(failure.errors?.pin?.[0] ?? failure.errors?.code?.[0] ?? failure.message)
      else setError(t('Online_Connect_cloud_error'))
    }
  }

  return (
    <Modal onClose={onClose} width={520} title={t('Online_Seal_RestoreTitle')}>
      <form className={styles.stack} onSubmit={(event) => void submit(event)} noValidate>
        <p className={styles.cardHelp}>{t('Online_Seal_RestoreHelp')}</p>

        <label className={styles.fieldLabel} htmlFor="seal-code">
          {t('Online_Seal_CodeLabel')}
        </label>
        <textarea
          id="seal-code"
          className={styles.linkBox}
          value={code}
          onChange={(event) => setCode(event.target.value)}
          rows={3}
          dir="ltr"
          spellCheck={false}
          autoComplete="off"
          maxLength={200}
          data-autofocus
        />

        <label className={styles.fieldLabel} htmlFor="seal-pin">
          {t('Online_Seal_PinLabel')}
        </label>
        <input
          id="seal-pin"
          className={styles.linkBox}
          type="password"
          inputMode="numeric"
          autoComplete="off"
          maxLength={32}
          value={pin}
          onChange={(event) => setPin(event.target.value)}
          aria-invalid={error ? true : undefined}
        />

        {error && (
          <p className={styles.alert} data-tone="danger" role="alert">
            {error}
          </p>
        )}

        <div className={styles.actions}>
          <Button variant="secondary" onClick={onClose}>
            {t('Online_Seal_NotNow')}
          </Button>
          <Button type="submit" variant="primary" loading={restore.isPending} disabled={!code.trim() || !pin}>
            {t('Online_Seal_Restore')}
          </Button>
        </div>
      </form>
    </Modal>
  )
}
