import { Link } from 'react-router-dom'
import { Icon } from './Icon'
import styles from './BackLink.module.css'

interface BackLinkProps {
  to: string
  children: string
}

/** The "back to the list" link that sits above a page title; the arrow mirrors itself in RTL. */
export function BackLink({ to, children }: BackLinkProps) {
  return (
    <Link className={styles.back} to={to}>
      <Icon name="arrow-start" size={14} />
      {children}
    </Link>
  )
}
