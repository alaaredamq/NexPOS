import { useTranslation } from 'react-i18next'
import { Modal } from '@/ui/Modal'
import { SHORTCUT_HELP } from './shortcuts'
import styles from './layout.module.css'

export function ShortcutsDialog({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation('pos')
  return (
    <Modal onClose={onClose} width={460} title={t('KeyboardShortcuts')}>
      <dl className={styles.shortcutList}>
        {SHORTCUT_HELP.map((shortcut) => (
          <div key={shortcut.keys} className={styles.shortcutRow}>
            <dt>
              <kbd className={styles.kbd}>{shortcut.keys}</kbd>
            </dt>
            <dd>{t(shortcut.labelKey)}</dd>
          </div>
        ))}
      </dl>
    </Modal>
  )
}
