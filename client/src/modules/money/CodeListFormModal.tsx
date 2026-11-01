import { useMemo } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSaveCodeList, type CodeList } from './api'
import styles from './money.module.css'
import { useFormRunner } from './useFormRunner'

interface FormValues {
  key: string
  name: string
  description: string
  displayOrder: number | null
  isActive: boolean
  allowMultipleSelection: boolean
}

interface Props {
  /** The list being edited; omit to create one. */
  list?: CodeList
  onClose: () => void
}

/** Create / edit dialog for a code list (its values have their own dialog). */
export function CodeListFormModal({ list, onClose }: Props) {
  const { t } = useTranslation(['money', 'common'])
  const save = useSaveCodeList()

  const schema = useMemo(
    () =>
      z.object({
        key: z.string().refine((v) => v.trim().length > 0, t('KeyRequired', { ns: 'money' })).refine((v) => v.trim().length <= 100, t('KeyTooLong', { ns: 'money' })),
        name: z.string().refine((v) => v.trim().length > 0, t('NameRequired', { ns: 'money' })).refine((v) => v.trim().length <= 200, t('NameTooLong', { ns: 'money' })),
        description: z.string().max(500, t('DescriptionTooLong', { ns: 'money' })),
        displayOrder: z.number({ error: t('OrderRange', { ns: 'money' }) }).nullable().refine((v) => v !== null && v >= 0 && v <= 100000, t('OrderRange', { ns: 'money' })),
        isActive: z.boolean(),
        allowMultipleSelection: z.boolean(),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: {
      key: list?.key ?? '',
      name: list?.name ?? '',
      description: list?.description ?? '',
      displayOrder: list?.displayOrder ?? 0,
      isActive: list?.isActive ?? true,
      allowMultipleSelection: list?.allowMultipleSelection ?? false,
    },
  })
  const { formErrors, run } = useFormRunner(form, t('common.actionFailed', { ns: 'common' }), () => {
    toast.error(t('CodeListNotFound', { ns: 'money' }))
    onClose()
  })

  const submit = form.handleSubmit((values) =>
    run(async () => {
      await save.mutateAsync({
        id: list?.id,
        input: {
          key: values.key.trim(),
          name: values.name.trim(),
          description: values.description.trim() || null,
          displayOrder: values.displayOrder as number,
          isActive: values.isActive,
          allowMultipleSelection: values.allowMultipleSelection,
        },
      })
      toast.success(list ? t('CodeListUpdated', { ns: 'money' }) : t('CodeListCreated', { ns: 'money' }))
      onClose()
    }),
  )

  return (
    <Modal onClose={onClose} width={620} title={list ? t('EditCodeList', { ns: 'money' }) : t('AddCodeList', { ns: 'money' })}>
      <form onSubmit={submit} noValidate className={styles.modalForm}>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <div className={styles.formRow}>
              <TextField<FormValues> control={form.control} name="key" dir="ltr" label={t('ListKey', { ns: 'money' })} hint={t('ListKeyHint', { ns: 'money' })} required maxLength={100} autoFocus autoComplete="off" />
              <TextField<FormValues> control={form.control} name="name" label={t('ListName', { ns: 'money' })} required maxLength={200} autoComplete="off" />
            </div>
            <TextField<FormValues> control={form.control} name="description" label={t('Description', { ns: 'common' })} multiline rows={2} maxLength={500} />
            <NumberField<FormValues> control={form.control} name="displayOrder" decimals={0} min={0} max={100000} label={t('DisplayOrder', { ns: 'money' })} required />
            <SwitchField<FormValues> control={form.control} name="allowMultipleSelection" label={t('AllowMultiple', { ns: 'money' })} hint={t('AllowMultipleHint', { ns: 'money' })} />
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('Active', { ns: 'common' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {list ? t('Save', { ns: 'common' }) : t('AddCodeList', { ns: 'money' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
