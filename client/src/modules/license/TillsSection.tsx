import { useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { ConfirmDialog } from '@/ui/ConfirmDialog'
import { formatDateTime } from '@/ui/format'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { useCreatePairingCode, useRemoveTill, useTillSeats, type PairingCode, type TillDevice } from './api'
import styles from './license.module.css'

const when = (value?: string | null) => (value ? formatDateTime(value) : '-')

/**
 * The PCs of a multi-seat package: how many are in use, each till with its state, and "Add a till", which reserves a seat and shows the
 * one-time code to type on that PC's pairing screen. The store server itself is the first seat and is never listed.
 */
export function TillsSection() {
  const { t } = useTranslation('common')
  const seats = useTillSeats(true)
  const create = useCreatePairingCode()
  const remove = useRemoveTill()
  const [name, setName] = useState('')
  const [issued, setIssued] = useState<PairingCode | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [removing, setRemoving] = useState<TillDevice | null>(null)

  if (!seats.data) return null
  const info = seats.data
  const full = info.tillSeats != null && info.used >= info.tillSeats
  const pairUrl = `${window.location.origin}/app/pair-till`

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    setIssued(null)
    try {
      setIssued(await create.mutateAsync(name.trim()))
      setName('')
    } catch (failure) {
      setError(isApiError(failure) ? failure.message : t('common.actionFailed'))
    }
  }

  return (
    <section className={styles.section} aria-labelledby="tills-heading">
      <h2 id="tills-heading" className={styles.heading}>
        {t('License_Tills_Title')}
      </h2>
      <p className={styles.hint}>
        {info.tillSeats == null
          ? t('License_Tills_Unlimited')
          : t('License_Tills_Usage', { used: info.used + 1, total: info.seats })}
      </p>

      {info.devices.length > 0 && (
        <ul className={styles.tills}>
          {info.devices.map((device) => (
            <li key={device.id} className={styles.till}>
              <div className={styles.tillMain}>
                <strong>
                  <bdi>{device.name}</bdi>
                </strong>
                <span className={styles.hint}>
                  {device.state === 'Active'
                    ? t('License_Tills_LastSeen', { when: when(device.lastSeenAt) })
                    : t('License_Tills_WaitingCode')}
                </span>
              </div>
              <StatusBadge
                tone={device.overflow ? 'danger' : device.state === 'Active' ? 'success' : 'warning'}
                label={device.overflow ? t('License_Tills_Overflow') : device.state === 'Active' ? t('License_Tills_Paired') : t('License_Tills_Waiting')}
              />
              <Button variant="ghost" onClick={() => setRemoving(device)}>
                {device.state === 'Active' ? t('License_Tills_Remove') : t('License_Tills_Cancel')}
              </Button>
            </li>
          ))}
        </ul>
      )}

      <form className={styles.addRow} onSubmit={(event) => void submit(event)} noValidate>
        <input
          value={name}
          onChange={(event) => setName(event.target.value)}
          maxLength={100}
          placeholder={t('License_Tills_NameHint')}
          aria-label={t('License_Tills_NameHint')}
          disabled={full}
        />
        <Button type="submit" variant="primary" loading={create.isPending} disabled={full || name.trim().length === 0}>
          {t('License_Tills_Add')}
        </Button>
      </form>
      {full && <p className={styles.hint}>{t('License_SeatsFull')}</p>}
      {error && (
        <p className={styles.alert} data-tone="danger" role="alert">
          {error}
        </p>
      )}

      {issued && (
        <div className={styles.codeBox} role="status">
          <span className={styles.hint}>{t('License_Tills_CodeIntro', { url: pairUrl })}</span>
          <strong className={styles.code} dir="ltr">
            {issued.code}
          </strong>
          <span className={styles.hint}>{t('License_Tills_CodeNote')}</span>
        </div>
      )}

      {removing && (
        <ConfirmDialog
          title={removing.state === 'Active' ? t('License_Tills_Remove') : t('License_Tills_Cancel')}
          message={t('License_Tills_RemoveConfirm', { name: removing.name })}
          detail={t('License_Tills_RemoveDetail')}
          confirmLabel={removing.state === 'Active' ? t('License_Tills_Remove') : t('License_Tills_Cancel')}
          danger
          onConfirm={async () => {
            await remove.mutateAsync(removing.id)
            toast.success(t('License_Tills_Removed'))
            setRemoving(null)
          }}
          onCancel={() => setRemoving(null)}
        />
      )}
    </section>
  )
}
