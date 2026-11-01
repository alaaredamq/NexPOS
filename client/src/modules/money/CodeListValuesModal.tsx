import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { confirm } from '@/ui/confirm'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { IconButton } from '@/ui/IconButton'
import { Loader } from '@/ui/Loader'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { StatusBadge } from '@/ui/StatusBadge'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useCodeList, useDeleteCodeListValue, useSaveCodeListValue, type CodeList, type CodeListValue } from './api'
import styles from './money.module.css'
import { useFormRunner } from './useFormRunner'

interface FormValues {
  value: string
  displayText: string
  description: string
  metadata: string
  displayOrder: number | null
  isActive: boolean
}

const EMPTY: FormValues = { value: '', displayText: '', description: '', metadata: '', displayOrder: 0, isActive: true }

interface Props {
  list: CodeList
  canManage: boolean
  onClose: () => void
}

/** The values of one code list: the table, and below it the form that adds a value or edits the one picked in the table. */
export function CodeListValuesModal({ list, canManage, onClose }: Props) {
  const { t } = useTranslation(['money', 'common'])
  const details = useCodeList(list.id)
  const save = useSaveCodeListValue(list.id)
  const remove = useDeleteCodeListValue(list.id)
  const [editing, setEditing] = useState<CodeListValue | null>(null)

  const schema = useMemo(
    () =>
      z.object({
        value: z.string().refine((v) => v.trim().length > 0, t('ValueRequired', { ns: 'money' })).refine((v) => v.trim().length <= 200, t('NameTooLong', { ns: 'money' })),
        displayText: z.string().max(200, t('NameTooLong', { ns: 'money' })),
        description: z.string().max(500, t('DescriptionTooLong', { ns: 'money' })),
        metadata: z.string().max(2000, t('MetadataTooLong', { ns: 'money' })),
        displayOrder: z.number({ error: t('OrderRange', { ns: 'money' }) }).nullable().refine((v) => v !== null && v >= 0 && v <= 100000, t('OrderRange', { ns: 'money' })),
        isActive: z.boolean(),
      }),
    [t],
  )
  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema as never), defaultValues: EMPTY })
  const { formErrors, run } = useFormRunner(form, t('common.actionFailed', { ns: 'common' }), () => {
    toast.error(t('ValueNotFound', { ns: 'money' }))
    setEditing(null)
  })

  // Picking a row loads it into the form; picking none (or finishing) goes back to an empty "add" form.
  useEffect(() => {
    form.reset(
      editing
        ? {
            value: editing.value,
            displayText: editing.displayText ?? '',
            description: editing.description ?? '',
            metadata: editing.metadata ?? '',
            displayOrder: editing.displayOrder,
            isActive: editing.isActive,
          }
        : EMPTY,
    )
  }, [editing, form])

  const submit = form.handleSubmit((values) =>
    run(async () => {
      await save.mutateAsync({
        id: editing?.id,
        input: {
          value: values.value.trim(),
          displayText: values.displayText.trim() || null,
          description: values.description.trim() || null,
          metadata: values.metadata.trim() || null,
          displayOrder: values.displayOrder as number,
          isActive: values.isActive,
        },
      })
      toast.success(editing ? t('ValueUpdated', { ns: 'money' }) : t('ValueAdded', { ns: 'money' }))
      setEditing(null)
    }),
  )

  const askDelete = async (value: CodeListValue) => {
    await confirm({
      title: t('ConfirmDelete', { ns: 'common' }),
      message: t('DeleteValueConfirm', { ns: 'money' }),
      detail: value.value,
      confirmLabel: t('Delete', { ns: 'common' }),
      danger: true,
      onConfirm: async () => {
        await remove.mutateAsync(value.id)
        if (editing?.id === value.id) setEditing(null)
        toast.success(t('ValueDeleted', { ns: 'money' }))
      },
    })
  }

  const values = details.data?.values ?? []

  return (
    <Modal onClose={onClose} width={820} title={t('ValuesOf', { ns: 'money', name: list.name })}>
      <ModalBody>
        <div className={styles.form}>
          {details.isPending && <Loader />}
          {details.isError && <p className={styles.warn}>{details.error.message}</p>}
          {details.data && values.length === 0 && <p className={styles.note}>{t('NoValues', { ns: 'money' })}</p>}
          {values.length > 0 && (
            <div className={styles.scroll}>
              <table className={styles.valueTable}>
                <caption className="sr-only">{t('ValuesOf', { ns: 'money', name: list.name })}</caption>
                <thead>
                  <tr>
                    <th scope="col">{t('Value', { ns: 'money' })}</th>
                    <th scope="col">{t('DisplayText', { ns: 'money' })}</th>
                    <th scope="col">{t('DisplayOrder', { ns: 'money' })}</th>
                    <th scope="col">{t('Status', { ns: 'common' })}</th>
                    {canManage && <th scope="col">{t('Actions', { ns: 'common' })}</th>}
                  </tr>
                </thead>
                <tbody>
                  {values.map((v) => (
                    <tr key={v.id}>
                      <td>
                        <bdi className={styles.strong}>{v.value}</bdi>
                      </td>
                      <td>{v.displayText ? <bdi>{v.displayText}</bdi> : <span className={styles.muted}>-</span>}</td>
                      <td>
                        <bdi>{v.displayOrder}</bdi>
                      </td>
                      <td>
                        <StatusBadge tone={v.isActive ? 'success' : 'neutral'} label={v.isActive ? t('Active', { ns: 'common' }) : t('Inactive', { ns: 'common' })} />
                      </td>
                      {canManage && (
                        <td>
                          <span className={styles.valueActions}>
                            <IconButton icon="edit" label={t('Edit', { ns: 'common' })} onClick={() => setEditing(v)} />
                            <IconButton icon="trash" tone="danger" label={t('Delete', { ns: 'common' })} onClick={() => void askDelete(v)} />
                          </span>
                        </td>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {canManage && (
            <form onSubmit={submit} noValidate aria-label={editing ? t('EditValue', { ns: 'money' }) : t('AddValue', { ns: 'money' })} className={styles.form}>
              <h3 className={styles.cardTitle}>{editing ? t('EditValue', { ns: 'money' }) : t('AddValue', { ns: 'money' })}</h3>
              <FormError messages={formErrors} />
              <div className={styles.valueForm}>
                <TextField<FormValues> control={form.control} name="value" dir="ltr" label={t('Value', { ns: 'money' })} required maxLength={200} autoComplete="off" />
                <TextField<FormValues> control={form.control} name="displayText" label={t('DisplayText', { ns: 'money' })} maxLength={200} autoComplete="off" />
                <NumberField<FormValues> control={form.control} name="displayOrder" decimals={0} min={0} max={100000} label={t('DisplayOrder', { ns: 'money' })} required />
              </div>
              <TextField<FormValues> control={form.control} name="description" label={t('Description', { ns: 'common' })} maxLength={500} />
              <TextField<FormValues> control={form.control} name="metadata" dir="ltr" label={t('Metadata', { ns: 'money' })} hint={t('MetadataHint', { ns: 'money' })} multiline rows={2} maxLength={2000} />
              <SwitchField<FormValues> control={form.control} name="isActive" label={t('Active', { ns: 'common' })} />
              <div>
                <Button type="submit" variant="primary" loading={save.isPending}>
                  {editing ? t('Save', { ns: 'common' }) : t('AddValue', { ns: 'money' })}
                </Button>{' '}
                {editing && (
                  <Button variant="ghost" onClick={() => setEditing(null)} disabled={save.isPending}>
                    {t('Cancel', { ns: 'common' })}
                  </Button>
                )}
              </div>
            </form>
          )}
        </div>
      </ModalBody>
      <ModalFooter>
        <Button variant="ghost" onClick={onClose}>
          {t('Close', { ns: 'common' })}
        </Button>
      </ModalFooter>
    </Modal>
  )
}
