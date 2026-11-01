import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { z } from 'zod'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { TextField } from '@/ui/form/TextField'
import { applyServerErrors } from '@/ui/form/serverErrors'
import { Modal, ModalBody, ModalFooter } from '@/ui/Modal'
import { toast } from '@/ui/toast'
import sales from '../_shared/sales.module.css'
import { zodResolver } from '../_shared/zodResolver'
import { useVoidReceipt } from './api'

interface FormValues {
  reason: string
}

interface Props {
  receipt: { id: number; receiptNumber: string }
  onClose: () => void
  /** Called after the void went through (the details page re-reads itself through the query cache). */
  onVoided?: () => void
}

/** Asks why, warns that voiding reverses the whole sale, then voids. The reason is required, as in both old dialogs. */
export function VoidReceiptDialog({ receipt, onClose, onVoided }: Props) {
  const { t } = useTranslation('receipts')
  const voidReceipt = useVoidReceipt()
  const [formErrors, setFormErrors] = useState<string[]>([])

  const schema = useMemo(
    () =>
      z.object({
        reason: z
          .string()
          .refine((v) => v.trim().length > 0, t('PleaseEnterAReasonForVoiding'))
          .refine((v) => v.length <= 500, t('details.reasonTooLong')),
      }),
    [t],
  )
  const form = useForm<FormValues>({ resolver: zodResolver<FormValues>(schema), defaultValues: { reason: '' } })

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    try {
      await voidReceipt.mutateAsync({ id: receipt.id, reason: values.reason.trim() })
      toast.success(t('ReceiptVoidedSuccessfully'))
      onVoided?.()
      onClose()
    } catch (error) {
      setFormErrors(isApiError(error) ? applyServerErrors(form, { errors: error.errors, detail: error.message }) : [t('FailedToVoidReceipt')])
    }
  })

  return (
    <Modal onClose={onClose} width={520} title={`${t('VoidReceipt')} · ${receipt.receiptNumber}`}>
      <form onSubmit={submit} noValidate>
        <ModalBody>
          <div className={sales.dialog}>
            <FormError messages={formErrors} />
            <TextField<FormValues>
              control={form.control}
              name="reason"
              label={t('ReasonForVoiding')}
              placeholder={t('EnterReasonForVoiding')}
              multiline
              rows={3}
              maxLength={500}
              required
              autoFocus
            />
            <p className={sales.help} data-tone="danger">
              {t('VoidWarning')}
            </p>
          </div>
        </ModalBody>
        <ModalFooter>
          <Button variant="ghost" onClick={onClose} disabled={voidReceipt.isPending}>
            {t('Cancel')}
          </Button>
          <Button type="submit" variant="danger" loading={voidReceipt.isPending}>
            {t('VoidReceipt')}
          </Button>
        </ModalFooter>
      </form>
    </Modal>
  )
}
