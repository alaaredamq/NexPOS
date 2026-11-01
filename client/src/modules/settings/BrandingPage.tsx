import { useEffect, useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useTranslation } from 'react-i18next'
import { isApiError } from '@/api/http'
import { Button } from '@/ui/Button'
import { FormError } from '@/ui/form/FormError'
import { TextField } from '@/ui/form/TextField'
import { Loader } from '@/ui/Loader'
import { toast } from '@/ui/toast'
import { useBrandingSettings, useSaveBranding, type BrandingSettings } from './api'
import { SettingsLayout } from './SettingsLayout'
import styles from './settings.module.css'

interface FormValues {
  appName: string
}

/** `/app/settings/branding` - the white-label app name, logo and favicon. */
export default function BrandingPage() {
  const query = useBrandingSettings()

  return (
    <SettingsLayout>
      {query.isPending && <Loader fullSection />}
      {query.isError && <p className={styles.error}>{query.error.message}</p>}
      {query.data && <BrandingForm current={query.data} />}
    </SettingsLayout>
  )
}

type Kind = 'logo' | 'favicon'

function BrandingForm({ current }: { current: BrandingSettings }) {
  const { t } = useTranslation(['settings', 'common'])
  const save = useSaveBranding()
  const form = useForm<FormValues>({ defaultValues: { appName: current.appName } })
  const [files, setFiles] = useState<Record<Kind, File | null>>({ logo: null, favicon: null })
  const [remove, setRemove] = useState<Record<Kind, boolean>>({ logo: false, favicon: false })
  const [fileErrors, setFileErrors] = useState<Partial<Record<Kind, string>>>({})
  const [formErrors, setFormErrors] = useState<string[]>([])
  // Remounting the file input (a new key) is how a picked file is cleared.
  const [inputKeys, setInputKeys] = useState<Record<Kind, number>>({ logo: 0, favicon: 0 })
  const resetInput = (kind: Kind) => setInputKeys((k) => ({ ...k, [kind]: k[kind] + 1 }))

  const dirty = form.formState.isDirty || files.logo !== null || files.favicon !== null || remove.logo || remove.favicon

  const choose = (kind: Kind, file: File | null) => {
    const allowed = kind === 'logo' ? current.logoExtensions : current.faviconExtensions
    let error: string | undefined
    if (file) {
      const extension = file.name.includes('.') ? file.name.slice(file.name.lastIndexOf('.')).toLowerCase() : ''
      if (!allowed.includes(extension)) error = t('Set_FileTypeNotAllowed', { ns: 'common', 0: extension, 1: allowed.join(', ') })
      else if (file.size > current.maxFileBytes) error = t('Set_FileTooLarge', { ns: 'common', 0: Math.floor(current.maxFileBytes / 1024 / 1024) })
    }
    setFileErrors((e) => ({ ...e, [kind]: error }))
    if (error) {
      resetInput(kind)
      setFiles((f) => ({ ...f, [kind]: null }))
      return
    }
    setFiles((f) => ({ ...f, [kind]: file }))
    if (file) setRemove((r) => ({ ...r, [kind]: false }))
  }

  const clear = (kind: Kind) => {
    resetInput(kind)
    setFileErrors((e) => ({ ...e, [kind]: undefined }))
    setFiles((f) => ({ ...f, [kind]: null }))
  }

  const submit = form.handleSubmit(async (values) => {
    setFormErrors([])
    setFileErrors({})
    try {
      const saved = await save.mutateAsync({ appName: values.appName.trim(), logo: files.logo, favicon: files.favicon, removeLogo: remove.logo, removeFavicon: remove.favicon })
      form.reset({ appName: saved.appName })
      clear('logo')
      clear('favicon')
      setRemove({ logo: false, favicon: false })
      toast.success(t('BrandingSaved', { ns: 'settings' }))
    } catch (error) {
      if (!isApiError(error)) {
        setFormErrors([t('common.actionFailed', { ns: 'common' })])
        return
      }
      const fields = error.errors ?? {}
      setFileErrors({ logo: fields.logo?.[0], favicon: fields.favicon?.[0] })
      if (fields.appName?.[0]) form.setError('appName', { type: 'server', message: fields.appName[0] })
      if (Object.keys(fields).length === 0) setFormErrors([error.message])
    }
  })

  return (
    <form onSubmit={submit} noValidate className={styles.stack}>
      <p className={styles.note}>{t('Set_BrandingSubtitle', { ns: 'common' })}</p>
      <FormError messages={formErrors} />

      <section className={styles.card} aria-labelledby="brand-title">
        <h2 id="brand-title" className={styles.cardTitle}>
          {t('Set_Branding', { ns: 'common' })}
        </h2>
        <div className={styles.brandGrid}>
          <TextField<FormValues>
            control={form.control}
            name="appName"
            label={t('Set_AppName', { ns: 'common' })}
            hint={t('Set_AppNameHelp', { ns: 'common', 0: current.defaultAppName })}
            placeholder={current.defaultAppName}
            maxLength={current.maxNameLength}
            autoComplete="off"
          />
          <ImageBox
            kind="logo"
            label={t('Set_Logo', { ns: 'common' })}
            help={t('Set_LogoHelp', { ns: 'common' })}
            alt={t('LogoPreviewAlt', { ns: 'settings' })}
            currentUrl={current.logoUrl}
            accept={current.logoExtensions.join(',')}
            file={files.logo}
            error={fileErrors.logo}
            hasCustom={current.hasCustomLogo}
            removed={remove.logo}
            removeLabel={t('Set_RemoveLogo', { ns: 'common' })}
            clearLabel={t('ClearSelection', { ns: 'settings' })}
            inputKey={inputKeys.logo}
            onChoose={(f) => choose('logo', f)}
            onClear={() => clear('logo')}
            onRemove={(v) => setRemove((r) => ({ ...r, logo: v }))}
          />
          <ImageBox
            kind="favicon"
            label={t('Set_Favicon', { ns: 'common' })}
            help={t('Set_FaviconHelp', { ns: 'common' })}
            alt={t('FaviconPreviewAlt', { ns: 'settings' })}
            currentUrl={current.faviconUrl}
            accept={current.faviconExtensions.join(',')}
            file={files.favicon}
            error={fileErrors.favicon}
            hasCustom={current.hasCustomFavicon}
            removed={remove.favicon}
            removeLabel={t('Set_RemoveFavicon', { ns: 'common' })}
            clearLabel={t('ClearSelection', { ns: 'settings' })}
            inputKey={inputKeys.favicon}
            onChoose={(f) => choose('favicon', f)}
            onClear={() => clear('favicon')}
            onRemove={(v) => setRemove((r) => ({ ...r, favicon: v }))}
          />
        </div>
      </section>

      <div className={styles.actions}>
        <Button type="submit" variant="primary" loading={save.isPending} disabled={!dirty}>
          {t('Set_Save', { ns: 'common' })}
        </Button>
      </div>
    </form>
  )
}

interface ImageBoxProps {
  kind: Kind
  label: string
  help: string
  alt: string
  currentUrl: string
  accept: string
  file: File | null
  error?: string
  hasCustom: boolean
  removed: boolean
  removeLabel: string
  clearLabel: string
  inputKey: number
  onChoose: (file: File | null) => void
  onClear: () => void
  onRemove: (value: boolean) => void
}

/** The image in use (or the one just picked, before it is saved), a file picker, and "remove" when a custom image exists. */
function ImageBox({ kind, label, help, alt, currentUrl, accept, file, error, hasCustom, removed, removeLabel, clearLabel, inputKey, onChoose, onClear, onRemove }: ImageBoxProps) {
  const previewUrl = useMemo(() => (file ? URL.createObjectURL(file) : null), [file])
  useEffect(() => () => void (previewUrl && URL.revokeObjectURL(previewUrl)), [previewUrl])
  const id = `brand-${kind}`

  return (
    <div className={styles.imageBox}>
      <label htmlFor={id} className={styles.imageLabel}>
        {label}
      </label>
      <div className={styles.imageFrame}>
        <img src={previewUrl ?? currentUrl} alt={alt} className={kind === 'logo' ? styles.logoImg : styles.faviconImg} />
      </div>
      <input
        key={inputKey}
        id={id}
        type="file"
        className={styles.file}
        accept={accept}
        aria-describedby={`${id}-help`}
        aria-invalid={error ? true : undefined}
        onChange={(e) => onChoose(e.target.files?.[0] ?? null)}
      />
      <span id={`${id}-help`} className={styles.note}>
        {help}
      </span>
      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
      {file && (
        <Button variant="ghost" onClick={onClear}>
          {clearLabel}
        </Button>
      )}
      {hasCustom && (
        <label className={styles.check}>
          <input type="checkbox" checked={removed} disabled={file !== null} onChange={(e) => onRemove(e.target.checked)} />
          {removeLabel}
        </label>
      )}
    </div>
  )
}
