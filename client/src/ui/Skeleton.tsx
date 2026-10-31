import type { CSSProperties } from 'react'
import styles from './Skeleton.module.css'

interface SkeletonProps {
  width?: CSSProperties['inlineSize']
  height?: CSSProperties['blockSize']
  /** Round (avatars, dots) instead of a rounded rectangle. */
  circle?: boolean
}

/** Placeholder block for content that is on its way. Decorative — pair with an `aria-busy` container. */
export function Skeleton({ width = '100%', height = 14, circle = false }: SkeletonProps) {
  return <span className={styles.skeleton} data-circle={circle || undefined} style={{ inlineSize: width, blockSize: height }} aria-hidden="true" />
}
