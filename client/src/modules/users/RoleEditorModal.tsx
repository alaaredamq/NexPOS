import { useEffect, useMemo, useRef, useState, type FormEvent } from 'react'
import { useTranslation } from 'react-i18next'
import { ALL_PERMISSIONS, Permissions, type Permission } from '@/api/permissions'
import { isApiError } from '@/api/http'
import { useSession } from '@/core/auth/useSession'
import { Button } from '@/ui/Button'
import { Loader } from '@/ui/Loader'
import { FormError } from '@/ui/form/FormError'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { useRole, useSaveRole } from './api'
import styles from './users.module.css'

interface Props {
  /** The role being edited; omit to create one. */
  roleId?: number
  onClose: () => void
}

interface Group {
  name: string
  items: { key: Permission; action: string }[]
}

/** The catalogue grouped by area ("Products", "Users", ...), areas A-Z. Keys are `Area.Action`. */
const GROUPS: Group[] = (() => {
  const byArea = new Map<string, Group['items']>()
  for (const key of ALL_PERMISSIONS) {
    const dot = key.indexOf('.')
    const area = dot > 0 ? key.slice(0, dot) : key
    const action = dot > 0 ? key.slice(dot + 1) : key
    byArea.set(area, [...(byArea.get(area) ?? []), { key, action }])
  }
  return [...byArea.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([name, items]) => ({ name, items }))
})()

/** A group's box: ticked when all of its permissions are, "partly" when some are. */
function GroupToggle({ group, selected, disabled, onToggle }: { group: Group; selected: ReadonlySet<string>; disabled: boolean; onToggle: (on: boolean) => void }) {
  const ref = useRef<HTMLInputElement>(null)
  const on = group.items.filter((i) => selected.has(i.key)).length
  useEffect(() => {
    if (ref.current) ref.current.indeterminate = on > 0 && on < group.items.length
  }, [on, group.items.length])

  return (
    <label className={styles.groupHead}>
      <input ref={ref} type="checkbox" checked={on === group.items.length} disabled={disabled} onChange={(e) => onToggle(e.target.checked)} />
      <bdi>{group.name}</bdi>
    </label>
  )
}

interface Initial {
  name: string
  description: string
  permissions: readonly string[]
  locked: boolean
}

/**
 * Create / edit a role with the permission matrix. The editor always sends the complete tick state; the server makes the
 * stored grants match it, and everyone holding the role is affected on their next request. SuperAdmin is shown read-only.
 */
export function RoleEditorModal({ roleId, onClose }: Props) {
  const { t } = useTranslation(['users', 'common'])
  const existing = useRole(roleId ?? null)
  const editing = roleId !== undefined
  const details = existing.data
  const locked = details?.role.isSuperAdmin === true
  const title = !editing ? t('AddRole', { ns: 'users' }) : locked ? t('ViewRole', { ns: 'users' }) : t('EditRole', { ns: 'users' })

  return (
    <Modal onClose={onClose} width={860} title={title}>
      {editing && !details ? (
        <>
          <ModalBody>{existing.isError ? <FormError messages={[existing.error.message]} /> : <Loader fullSection />}</ModalBody>
          <ModalFooter>
            <Button variant="ghost" onClick={onClose}>
              {t('Cancel', { ns: 'common' })}
            </Button>
          </ModalFooter>
        </>
      ) : (
        <RoleForm
          roleId={roleId}
          onClose={onClose}
          initial={{ name: details?.role.name ?? '', description: details?.role.description ?? '', permissions: details?.permissions ?? [], locked }}
        />
      )}
    </Modal>
  )
}

function RoleForm({ roleId, initial, onClose }: { roleId?: number; initial: Initial; onClose: () => void }) {
  const { t } = useTranslation(['users', 'common'])
  const session = useSession()
  const save = useSaveRole()
  const editing = roleId !== undefined
  const locked = initial.locked

  const [name, setName] = useState(initial.name)
  const [description, setDescription] = useState(initial.description)
  const [selected, setSelected] = useState<ReadonlySet<string>>(() => new Set(initial.permissions))
  const [nameError, setNameError] = useState<string | null>(null)
  const [formErrors, setFormErrors] = useState<string[]>([])
  const total = ALL_PERMISSIONS.length
  const losesOwnAccess = useMemo(
    () => editing && roleId === session.role.id && !selected.has(Permissions.Users_Manage),
    [editing, roleId, session.role.id, selected],
  )

  const setMany = (keys: readonly string[], on: boolean) =>
    setSelected((prev) => {
      const next = new Set(prev)
      for (const k of keys) {
        if (on) next.add(k)
        else next.delete(k)
      }
      return next
    })

  const submit = async (e: FormEvent) => {
    e.preventDefault()
    setFormErrors([])
    setNameError(null)
    if (name.trim() === '') {
      setNameError(t('RoleNameRequired', { ns: 'users' }))
      return
    }
    try {
      await save.mutateAsync({
        id: roleId,
        input: { name: name.trim(), description: description.trim() || null, permissions: ALL_PERMISSIONS.filter((p) => selected.has(p)) },
      })
      toast.success(editing ? t('RoleUpdated', { ns: 'users' }) : t('RoleCreated', { ns: 'users' }))
      onClose()
    } catch (error) {
      if (!isApiError(error)) {
        setFormErrors([t('common.actionFailed', { ns: 'common' })])
        return
      }
      if (error.status === 404) {
        toast.error(error.message)
        onClose()
        return
      }
      const nameMessage = error.errors?.name?.[0]
      if (nameMessage) setNameError(nameMessage)
      else setFormErrors([error.message])
    }
  }

  return (
    <form onSubmit={(e) => void submit(e)} noValidate>
      <ModalBody>
        <div className={styles.form}>
          <FormError messages={formErrors} />
          <div className={styles.roleHead}>
            <div className={styles.pinField}>
              <label htmlFor="role-name">
                {t('RoleName', { ns: 'users' })}
                <span aria-hidden="true"> *</span>
              </label>
              <input id="role-name" className={styles.textInput} value={name} maxLength={100} disabled={locked} data-autofocus aria-invalid={nameError ? true : undefined} aria-describedby={nameError ? 'role-name-error' : undefined} onChange={(e) => setName(e.target.value)} />
              {nameError && (
                <small id="role-name-error" role="alert" className={styles.error}>
                  {nameError}
                </small>
              )}
            </div>
            <div className={styles.pinField}>
              <label htmlFor="role-description">{t('DescriptionOptional', { ns: 'users' })}</label>
              <input id="role-description" className={styles.textInput} value={description} maxLength={500} disabled={locked} onChange={(e) => setDescription(e.target.value)} />
            </div>
          </div>

          <div className={styles.matrixTitle}>
            <span>{t('Permissions', { ns: 'users' })}</span>
            <span className={styles.muted} aria-live="polite">
              {t('PermissionsSelected', { ns: 'users', count: selected.size, total })}
            </span>
          </div>

          <div className={styles.matrix}>
            {GROUPS.map((group) => (
              <fieldset key={group.name} className={styles.group}>
                <legend className={styles.srOnly}>{group.name}</legend>
                <GroupToggle group={group} selected={selected} disabled={locked} onToggle={(on) => setMany(group.items.map((i) => i.key), on)} />
                <div className={styles.actions}>
                  {group.items.map((item) => (
                    <label key={item.key} className={styles.check}>
                      <input type="checkbox" checked={selected.has(item.key)} disabled={locked} onChange={(e) => setMany([item.key], e.target.checked)} />
                      <bdi>{item.action}</bdi>
                    </label>
                  ))}
                </div>
              </fieldset>
            ))}
          </div>

          {locked && <p className={styles.note}>{t('SuperAdminNote', { ns: 'users' })}</p>}
          {losesOwnAccess && <p className={styles.warn}>{t('OwnAccessWarning', { ns: 'users' })}</p>}
        </div>
      </ModalBody>
      <ModalFooter>
        <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
          {locked ? t('Close', { ns: 'common' }) : t('Cancel', { ns: 'common' })}
        </Button>
        {!locked && (
          <Button type="submit" variant="primary" loading={save.isPending}>
            {t('SaveRole', { ns: 'users' })}
          </Button>
        )}
      </ModalFooter>
    </form>
  )
}
