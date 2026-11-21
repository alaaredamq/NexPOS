import { useState } from 'react'
import styles from './checkout.module.css'

/**
 * A payment method's label: the wallet company's logo when it has one (today only Whish), else its name. The logo files live in
 * `client/public/payment-logos/` (whish.svg is the official one from whish.money). The logo already spells the name, so once it has loaded
 * the name is only read out to screen readers; if the file is missing or fails, the plain name shows as before.
 */
const SOURCES: Record<string, string[]> = {
  Whish: ['payment-logos/whish.svg', 'payment-logos/whish.png'],
}

export function PaymentMethodLabel({ name, provider }: { name: string; provider: string | null | undefined }) {
  const sources = provider ? SOURCES[provider] : undefined
  const [attempt, setAttempt] = useState(0)
  const [loaded, setLoaded] = useState(false)
  const source = sources?.[attempt]

  return (
    <>
      {source && (
        <img
          key={source}
          className={styles.methodLogo}
          src={`${import.meta.env.BASE_URL}${source}`}
          alt=""
          aria-hidden="true"
          draggable={false}
          data-loaded={loaded || undefined}
          onLoad={() => setLoaded(true)}
          onError={() => {
            setLoaded(false)
            setAttempt((n) => n + 1)
          }}
        />
      )}
      <span className={loaded ? 'sr-only' : undefined}>{name}</span>
    </>
  )
}
