import { useCallback, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { useNavigate } from 'react-router-dom'
import { isApiError } from '@/api/http'
import { useSession } from '@/core/auth/useSession'
import { landingPath } from '@/shell/nav'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { PageHeader } from '@/ui/PageHeader'
import { PinPad } from '@/ui/PinPad'
import { toast } from '@/ui/toast'
import { useChangePin } from './api'
import styles from './account.module.css'

const LENGTH = 4
type FieldName = 'currentPin' | 'newPin' | 'confirmPin'

/**
 * `/app/account/change-pin` - change your own PIN with the on-screen keypad. Right after an admin set (or reset) the PIN the current
 * one is not asked for, and the rest of the app sends you here until you have chosen your own.
 */
export default function ChangePinPage() {
  const { t } = useTranslation(['account', 'common'])
  const navigate = useNavigate()
  const session = useSession()
  const forced = session.user.mustChangePin
  const change = useChangePin()

  const fields: FieldName[] = forced ? ['newPin', 'confirmPin'] : ['currentPin', 'newPin', 'confirmPin']
  const labels: Record<FieldName, string> = {
    currentPin: t('Current PIN', { ns: 'common' }),
    newPin: t('New PIN', { ns: 'common' }),
    confirmPin: t('Confirm New PIN', { ns: 'common' }),
  }

  const [values, setValues] = useState<Record<FieldName, string>>({ currentPin: '', newPin: '', confirmPin: '' })
  const [active, setActive] = useState<FieldName>(fields[0]!)
  const [errors, setErrors] = useState<Partial<Record<FieldName, string>>>({})
  const [formErrors, setFormErrors] = useState<string[]>([])

  const onKey = useCallback(
    (key: string) => {
      if (key === 'Enter') return
      setValues((prev) => {
        const current = prev[active]
        const next = key === 'C' ? '' : key === '⌫' ? current.slice(0, -1) : /^\d$/.test(key) && current.length < LENGTH ? current + key : current
        return next === current ? prev : { ...prev, [active]: next }
      })
      setErrors((prev) => (prev[active] ? { ...prev, [active]: undefined } : prev))
      // A full field hands the keypad to the next empty one.
      if (/^\d$/.test(key) && values[active].length + 1 === LENGTH) {
        const next = fields[fields.indexOf(active) + 1]
        if (next && values[next].length < LENGTH) setActive(next)
      }
    },
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [active, values, forced],
  )

  const submit = async () => {
    setFormErrors([])
    const found: Partial<Record<FieldName, string>> = {}
    for (const f of fields) if (values[f].length !== LENGTH) found[f] = t('Pin_Rule4', { ns: 'common' })
    if (!found.confirmPin && values.newPin !== values.confirmPin) found.confirmPin = t('PINs do not match', { ns: 'common' })
    if (Object.keys(found).length > 0) {
      setErrors(found)
      setActive(fields.find((f) => found[f]) ?? active)
      return
    }
    try {
      await change.mutateAsync({ currentPin: forced ? null : values.currentPin, newPin: values.newPin, confirmPin: values.confirmPin })
      toast.success(t('PinUpdated', { ns: 'account' }))
      void navigate(forced ? (landingPath(session) ?? '/account/profile') : '/account/profile', { replace: true })
    } catch (error) {
      if (!isApiError(error)) {
        setFormErrors([t('common.actionFailed', { ns: 'common' })])
        return
      }
      const server: Partial<Record<FieldName, string>> = {}
      for (const f of fields) if (error.errors?.[f]?.[0]) server[f] = error.errors[f]![0]
      if (Object.keys(server).length === 0) {
        setFormErrors([error.message])
        return
      }
      setErrors(server)
      // The wrong value is cleared; the keypad goes back to the first field that was refused.
      const first = fields.find((f) => server[f]) ?? active
      setValues((prev) => ({ ...prev, [first]: '', ...(first === 'newPin' ? { confirmPin: '' } : {}) }))
      setActive(first)
    }
  }

  return (
    <div className={`${styles.page} ${styles.narrow}`}>
      <PageHeader title={t('ChangePin', { ns: 'common' })} />
      <section className={styles.pinCard}>
        <p className={styles.lead}>{forced ? t('ChangePinRequired', { ns: 'account' }) : t('Pin_UpdateYourLogin', { ns: 'common' })}</p>
        <FormError messages={formErrors} />
        <div className={styles.pinFields} role="radiogroup" aria-label={t('ChangePin', { ns: 'common' })}>
          {fields.map((f) => (
            <button
              key={f}
              type="button"
              role="radio"
              aria-checked={active === f}
              className={styles.pinField}
              data-active={active === f}
              data-invalid={errors[f] ? true : undefined}
              onClick={() => setActive(f)}
            >
              <span className={styles.pinLabel}>{labels[f]}</span>
              <span className={styles.dots} dir="ltr" aria-hidden="true">
                {Array.from({ length: LENGTH }).map((_, i) => (
                  <span key={i} className={styles.dot} data-filled={i < values[f].length} />
                ))}
              </span>
              {errors[f] && (
                <span className={styles.fieldError} role="alert">
                  {errors[f]}
                </span>
              )}
            </button>
          ))}
        </div>
        <PinPad pin={values[active]} onKey={onKey} dotCount={LENGTH} />
        <ul className={styles.rules}>
          <li>{t('Pin_Rule4', { ns: 'common' })}</li>
          {!forced && <li>{t('Pin_RuleDifferent', { ns: 'common' })}</li>}
          <li>{t('Pin_RuleSequences', { ns: 'common' })}</li>
        </ul>
        <div className={styles.pinActions}>
          {!forced && (
            <Button variant="ghost" onClick={() => void navigate('/account/profile')} disabled={change.isPending}>
              {t('Cancel', { ns: 'common' })}
            </Button>
          )}
          <Button variant="primary" onClick={() => void submit()} loading={change.isPending}>
            {t('Pin_UpdateButton', { ns: 'common' })}
          </Button>
        </div>
      </section>
    </div>
  )
}
