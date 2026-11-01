import { useState } from 'react'
import styles from './displayOrder.module.css'

/** Product picture (this page and the products forms still show them; the POS tiles do not) or a neutral box when there is none / it fails to load. */
export function Thumb({ src, size = 44 }: { src?: string | null; size?: number }) {
  const [failed, setFailed] = useState(false)
  const box = { inlineSize: size, blockSize: size }

  return src && !failed ? (
    <img className={styles.thumb} style={box} src={src} alt="" loading="lazy" decoding="async" onError={() => setFailed(true)} />
  ) : (
    <span className={styles.thumbEmpty} style={box} aria-hidden="true" />
  )
}
