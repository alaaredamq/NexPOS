import { useEffect, useRef, useState } from 'react'
import { useTranslation } from 'react-i18next'
import { Link } from 'react-router-dom'
import { useSession } from '@/core/auth/useSession'
import { useSignOut } from '@/core/auth/useSignOut'
import styles from './TopBar.module.css'

/** The signed-in identity and the way out. Profile and change-PIN are the account pages of track A1. */
export function UserMenu() {
  const { t } = useTranslation()
  const { user, role } = useSession()
  const signOut = useSignOut()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  return (
    <div className={styles.userWrap} ref={ref}>
      <button type="button" className={styles.userChip} onClick={() => setOpen((o) => !o)} aria-haspopup="menu" aria-expanded={open}>
        <span className={styles.avatar}>{user.initials}</span>
        <span className={styles.userText}>
          <span className={styles.userName}>
            <bdi>{user.fullName}</bdi>
          </span>
          <span className={styles.userRole}>
            <bdi>{role.name}</bdi>
          </span>
        </span>
      </button>
      {open && (
        <div className={styles.menu} role="menu">
          <div className={styles.menuHeader}>
            <div className={styles.menuName}>
              <bdi>{user.fullName}</bdi>
            </div>
            <div className={styles.menuRole}>
              <bdi>{role.name}</bdi>
            </div>
          </div>
          <Link className={styles.menuLink} role="menuitem" to="/account/profile" onClick={() => setOpen(false)}>
            {t('Profile')}
          </Link>
          <Link className={styles.menuLink} role="menuitem" to="/account/change-pin" onClick={() => setOpen(false)}>
            {t('Shell_ChangePin')}
          </Link>
          <button type="button" className={styles.menuItem} role="menuitem" onClick={() => void signOut({ forget: true })}>
            {t('Logout')}
          </button>
        </div>
      )}
    </div>
  )
}
