import { useMemo, useState } from 'react'
import { useController, useForm, useWatch, type Control } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { Field } from '@/ui/form/Field'
import fieldStyles from '@/ui/form/field.module.css'
import { FormError } from '@/ui/form/FormError'
import { NumberField } from '@/ui/form/NumberField'
import { SelectField } from '@/ui/form/SelectField'
import { SwitchField } from '@/ui/form/SwitchField'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import { zodResolver } from '../_shared/zodResolver'
import { useSaveSchedule, type ScheduleRow } from './api'
import styles from './backup.module.css'

export const FREQUENCIES = ['Daily', 'Weekly', 'Monthly'] as const
export const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'] as const
const TIME = /^([01]\d|2[0-3]):[0-5]\d$/

interface FormValues {
  name: string
  description: string
  frequency: string
  time: string
  dayOfWeek: number | null
  dayOfMonth: number | null
  retentionDays: number | null
  isActive: boolean
}

/** A native time picker (touch-friendly, 24-hour value) in the form's chrome. */
function TimeField({ control, label, required }: { control: Control<FormValues>; label: string; required?: boolean }) {
  const { field, fieldState } = useController({ control, name: 'time' })
  return (
    <Field label={label} required={required} error={fieldState.error?.message}>
      {(a11y) => <input {...field} {...a11y} type="time" step={60} dir="ltr" className={fieldStyles.control} />}
    </Field>
  )
}

/** Create / edit a schedule. A weekly schedule needs its day of the week, a monthly one its day of the month (1-28: every month has them). */
export function ScheduleFormModal({ schedule, onClose }: { schedule?: ScheduleRow; onClose: () => void }) {
  const { t } = useTranslation(['backup', 'common'])
  const save = useSaveSchedule()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z
        .object({
          name: z.string().refine((v) => v.trim().length > 0, t('PleaseEnterScheduleName')),
          description: z.string(),
          frequency: z.string(),
          time: z.string().regex(TIME, t('Schedule_TimeRequired')),
          dayOfWeek: z.number().nullable(),
          dayOfMonth: z.number().nullable(),
          retentionDays: z
            .number()
            .int()
            .min(1, t('Schedule_RetentionRule'))
            .max(3650, t('Schedule_RetentionRule'))
            .nullable(),
          isActive: z.boolean(),
        })
        .superRefine((values, context) => {
          if (values.frequency === 'Weekly' && values.dayOfWeek === null) context.addIssue({ code: 'custom', path: ['dayOfWeek'], message: t('Schedule_DayRequired') })
          if (values.frequency === 'Monthly' && values.dayOfMonth === null) context.addIssue({ code: 'custom', path: ['dayOfMonth'], message: t('Schedule_DayRequired') })
        }),
    [t],
  )

  const form = useForm<FormValues>({
    resolver: zodResolver<FormValues>(schema as never),
    defaultValues: {
      name: schedule?.name ?? '',
      description: schedule?.description ?? '',
      frequency: schedule?.frequency ?? 'Daily',
      time: schedule?.time ?? '02:00',
      dayOfWeek: schedule?.dayOfWeek ?? 0,
      dayOfMonth: schedule?.dayOfMonth ?? 1,
      retentionDays: schedule?.retentionDays ?? null,
      isActive: schedule?.isActive ?? true,
    },
  })
  const frequency = useWatch({ control: form.control, name: 'frequency' })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await save.mutateAsync({
        id: schedule?.id,
        input: {
          name: values.name.trim(),
          description: values.description.trim() || null,
          frequency: values.frequency,
          time: values.time,
          dayOfWeek: values.frequency === 'Weekly' ? values.dayOfWeek : null,
          dayOfMonth: values.frequency === 'Monthly' ? values.dayOfMonth : null,
          retentionDays: values.retentionDays,
          isActive: values.isActive,
        },
      })
      toast.success(schedule ? t('ScheduleUpdatedSuccessfully') : t('ScheduleCreatedSuccessfully'))
      onClose()
    } catch (error) {
      if (isApiError(error) && error.status === 404) {
        toast.error(error.message)
        onClose()
        return
      }
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('FailedToSaveSchedule')])
    }
  })

  return (
    <Modal onClose={onClose} width={620} title={schedule ? t('EditSchedule') : t('CreateSchedule')}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={styles.form}>
            <FormError messages={formErrors} />
            <TextField<FormValues> control={form.control} name="name" label={t('ScheduleName')} placeholder={t('EnterScheduleName')} required maxLength={200} autoComplete="off" autoFocus />
            <TextField<FormValues> control={form.control} name="description" label={t('Description')} placeholder={t('EnterScheduleDescription')} multiline rows={2} maxLength={1000} />
            <div className={styles.formRow}>
              <SelectField<FormValues, string>
                control={form.control}
                name="frequency"
                label={t('Frequency', { ns: 'common' })}
                required
                options={FREQUENCIES.map((value) => ({ value, label: t(value, { ns: 'common' }) }))}
              />
              <TimeField control={form.control} label={t('Time')} required />
            </div>
            {frequency === 'Weekly' && (
              <SelectField<FormValues, number>
                control={form.control}
                name="dayOfWeek"
                label={t('DayOfWeek')}
                required
                options={WEEKDAYS.map((day, value) => ({ value, label: t(day) }))}
              />
            )}
            {frequency === 'Monthly' && (
              <SelectField<FormValues, number>
                control={form.control}
                name="dayOfMonth"
                label={t('DayOfMonth')}
                required
                options={Array.from({ length: 28 }, (_, i) => ({ value: i + 1, label: String(i + 1) }))}
              />
            )}
            <NumberField<FormValues>
              control={form.control}
              name="retentionDays"
              label={t('RetentionDays')}
              hint={t('RetentionDaysHint')}
              placeholder={t('RetentionDaysPlaceholder')}
              decimals={0}
              min={1}
              max={3650}
            />
            <SwitchField<FormValues> control={form.control} name="isActive" label={t('ActiveSchedule')} hint={t('ActiveScheduleHint')} />
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={save.isPending}>
            {t('Cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={save.isPending}>
            {t('Save')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
