import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSaveCategory, type Category } from './api'
import styles from './categories.module.css'

interface FormValues {
  name: string
  description: string
  isActive: boolean
}

interface Props {
  /** The category being edited; omit to create one. */
  category?: Pick<Category, 'id' | 'name' | 'description' | 'isActive'>
  onClose: () => void
}

/** Create / edit dialog. A category is a name, a description and an on/off switch - it has no colour or picture. */
export function CategoryFormModal({ category, onClose }: Props) {
  const { t } = useTranslation(['categories', 'common'])
  const save = useSaveCategory()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        name: z.string().refine((v) => v.trim().length >= 2 && v.trim().length <= 100, t('NameLengthError', { ns: 'categories' })),
        description: z.string().max(500, t('DescriptionTooLong', { ns: 'categories' })),
        isActive: z.boolean(),
      }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema),
    defaultValues: { name: category?.name ?? '', description: category?.description ?? '', isActive: category?.isActive ?? true },
  })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await save.mutateAsync({
        id: category?.id,
        input: { name: values.name.trim(), description: values.description.trim() || null, isActive: values.isActive },
      })
      toast.success(category ? t('CategoryUpdated', { ns: 'categories' }) : t('CategoryCreated', { ns: 'categories' }))
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        // Deleted by someone else while the dialog was open.
        toast.error(t('CategoryNotFound', { ns: 'categories' }))
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('common.actionFailed', { ns: 'common' })])
    }
  })

  return (
    <Modal onClose={onClose} width={520} title={category ? t('EditCategory', { ns: 'categories' }) : t('AddCategory', { ns: 'categories' })}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <TextField<FormValues> control={form.control} name="name" label={t('CategoryName', { ns: 'categories' })} required autoFocus maxLength={100} />
            <TextField<FormValues> control={form.control} name="description" label={t('Description', { ns: 'categories' })} multiline rows={4} maxLength={500} />
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('CategoryIsActive', { ns: 'categories' })} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel', { ns: 'common' })}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {t('Save', { ns: 'common' })}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
