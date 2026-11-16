import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Field } from '@/ui/form/Field'
import fieldStyles from '@/ui/form/field.module.css'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { useAddSection, useRenameSection, type OnlineMenuSection } from './api'
import styles from './onlineMenu.module.css'

interface Props {
  /** The section to rename; omit to add one. */
  section?: OnlineMenuSection
  onClose: () => void
  onCreated: (id: number) => void
}

/** Name a new section, or rename one. A section is only a name; what it shows is picked afterwards. */
export function SectionNameModal({ section, onClose, onCreated }: Props) {
  const { t } = useTranslation('onlineMenu')
  const add = useAddSection()
  const rename = useRenameSection()
  const [name, setName] = useState(section?.name ?? '')
  const [error, setError] = useState<string>()
  const busy = add.isPending || rename.isPending

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    const clean = name.trim()
    if (!clean) {
      setError(t('NameRequired'))
      return
    }
    try {
      if (section) {
        await rename.mutateAsync({ id: section.id, name: clean })
        toast.success(t('SectionRenamed'))
      } else {
        const created = await add.mutateAsync(clean)
        onCreated(created.id)
        toast.success(t('SectionAdded'))
      }
      onClose()
    } catch (err) {
      setError(isApiError(err) ? err.message : t('SomethingWentWrong'))
    }
  }

  return (
    <Modal onClose={onClose} width={460} title={section ? t('RenameSection') : t('NewSection')}>
      <form onSubmit={(e) => void submit(e)}>
        <ModalBody>
          <div className={styles.form}>
            <Field label={t('SectionName')} hint={section ? undefined : t('SectionNameHint')} required error={error}>
              {(a11y) => (
                <input
                  {...a11y}
                  className={fieldStyles.control}
                  value={name}
                  maxLength={120}
                  dir="auto"
                  autoFocus
                  onChange={(e) => {
                    setName(e.target.value)
                    setError(undefined)
                  }}
                />
              )}
            </Field>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" type="button" onClick={onClose}>
            {t('Cancel')}
          </Button>
          <Button variant="primary" type="submit" loading={busy}>
            {section ? t('Save') : t('AddSection')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
