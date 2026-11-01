import { useId, type ReactNode } from 'react'
import { useTranslation } from 'react-i18next'
import { Button } from '@/ui/Button'
import { ChipGroup } from '@/ui/ChipGroup'
import { SearchBox } from '@/ui/SearchBox'
import { Select, type SelectOption } from '@/ui/Select'
import { CATEGORIES, OUTCOMES, SEVERITIES, useAuditOptions, type AuditParams } from './api'
import { categoryKey, outcomeKey, severityKey, toLocalInput } from './badges'
import { hasFilters } from './listParams'
import styles from './auditLogs.module.css'

interface FiltersProps {
  params: AuditParams
  /** Applies a change and goes back to page 1. */
  onChange: (patch: Partial<AuditParams>) => void
  onReset: () => void
}

function Labeled({ label, htmlFor, children }: { label: string; htmlFor: string; children: ReactNode }) {
  return (
    <div className={styles.filter}>
      <label className={styles.filterLabel} htmlFor={htmlFor}>
        {label}
      </label>
      {children}
    </div>
  )
}

/** The picked option for a value the option list may not contain (yet): the value itself is the best label we have. */
const pick = (options: SelectOption<string>[], value: string, fallbackLabel = value): SelectOption<string> | null =>
  value ? (options.find((o) => o.value === value) ?? { value, label: fallbackLabel }) : null

type RangeKey = 'hour' | 'today' | '7d' | '30d' | 'all'

function rangeStart(range: RangeKey, now = new Date()): Date | null {
  if (range === 'hour') return new Date(now.getTime() - 3600_000)
  if (range === 'today') return new Date(now.getFullYear(), now.getMonth(), now.getDate())
  if (range === '7d') return new Date(now.getTime() - 7 * 86_400_000)
  if (range === '30d') return new Date(now.getTime() - 30 * 86_400_000)
  return null
}

/** Search, time window and the drop-downs. Everything applies as soon as it is chosen; free text waits for the typing to stop. */
export function AuditFilters({ params, onChange, onReset }: FiltersProps) {
  const { t } = useTranslation('auditLogs')
  const id = useId()
  const options = useAuditOptions()

  // The record number is committed on Enter / leaving the box, so typing "123" does not fire three requests. The box is
  // uncontrolled and re-keyed by the committed value, so a reset or a drill-down from the drawer shows the new number.
  const commitRecord = (typed: string) => {
    const clean = typed.trim()
    if (clean !== params.entityId && (clean === '' || /^\d+$/.test(clean))) onChange({ entityId: clean })
  }

  const users: SelectOption<string>[] = (options.data?.users ?? []).map((u) => ({
    value: u.userId ? String(u.userId) : `name:${u.username}`,
    label: u.username === 'System' ? t('Msg_System') : u.username,
  }))
  const categories = CATEGORIES.map((c) => ({ value: c, label: t(categoryKey(c) as string) }))
  const outcomes = OUTCOMES.map((o) => ({ value: o, label: t(outcomeKey(o) as string) }))
  const severities = SEVERITIES.map((s) => ({ value: s, label: `${t(severityKey(s) as string)}${s === 'Critical' ? '' : ' +'}` }))
  const entityTypes = (options.data?.entityTypes ?? []).map((v) => ({ value: v, label: v }))
  const actions = (options.data?.actions ?? []).map((v) => ({ value: v, label: v }))

  const ranges: { value: RangeKey; label: string }[] = [
    { value: 'hour', label: t('Range_Hour') },
    { value: 'today', label: t('Range_Today') },
    { value: '7d', label: t('Range_7d') },
    { value: '30d', label: t('Range_30d') },
    { value: 'all', label: t('Range_All') },
  ]
  const applyRange = (range: string) => {
    const start = rangeStart(range as RangeKey)
    onChange({ from: start ? toLocalInput(start) : '', to: '' })
  }

  return (
    <section className={styles.filters} aria-label={t('Filters')}>
      <div className={styles.filterRow}>
        <div className={styles.search}>
          <SearchBox value={params.q} onSearch={(q) => onChange({ q })} placeholder={t('Filter_SearchPlaceholder')} aria-label={t('Filter_Search')} />
        </div>
        <Labeled label={t('Filter_From')} htmlFor={`${id}-from`}>
          <input id={`${id}-from`} type="datetime-local" className={styles.input} value={params.from} max={params.to || undefined} onChange={(e) => onChange({ from: e.target.value })} />
        </Labeled>
        <Labeled label={t('Filter_To')} htmlFor={`${id}-to`}>
          <input id={`${id}-to`} type="datetime-local" className={styles.input} value={params.to} min={params.from || undefined} onChange={(e) => onChange({ to: e.target.value })} />
        </Labeled>
        <ChipGroup size="sm" aria-label={t('Filter_Range')} options={ranges} value="" onChange={applyRange} />
      </div>

      <div className={styles.filterRow}>
        <Labeled label={t('Filter_User')} htmlFor={`${id}-user`}>
          <Select<string>
            id={`${id}-user`}
            clearable
            options={users}
            value={pick(users, params.user, params.user.replace(/^name:/, ''))}
            placeholder={t('Filter_AllUsers')}
            onChange={(o) => onChange({ user: o?.value ?? '' })}
          />
        </Labeled>
        <Labeled label={t('Filter_Category')} htmlFor={`${id}-category`}>
          <Select<string> id={`${id}-category`} clearable options={categories} value={pick(categories, params.category)} placeholder={t('Filter_AllCategories')} onChange={(o) => onChange({ category: o?.value ?? '' })} />
        </Labeled>
        <Labeled label={t('Filter_Outcome')} htmlFor={`${id}-outcome`}>
          <Select<string> id={`${id}-outcome`} clearable options={outcomes} value={pick(outcomes, params.outcome)} placeholder={t('Filter_AllOutcomes')} onChange={(o) => onChange({ outcome: o?.value ?? '' })} />
        </Labeled>
        <Labeled label={t('Filter_Severity')} htmlFor={`${id}-severity`}>
          <Select<string> id={`${id}-severity`} clearable options={severities} value={pick(severities, params.severity)} placeholder={t('Filter_AnySeverity')} onChange={(o) => onChange({ severity: o?.value ?? '' })} />
        </Labeled>
        <Labeled label={t('Filter_EntityType')} htmlFor={`${id}-type`}>
          <Select<string> id={`${id}-type`} clearable options={entityTypes} value={pick(entityTypes, params.entityType)} placeholder={t('Filter_AllEntities')} onChange={(o) => onChange({ entityType: o?.value ?? '' })} />
        </Labeled>
        <Labeled label={t('Filter_RecordId')} htmlFor={`${id}-record`}>
          <input
            key={params.entityId}
            id={`${id}-record`}
            className={styles.input}
            inputMode="numeric"
            defaultValue={params.entityId}
            onBlur={(e) => commitRecord(e.currentTarget.value)}
            onKeyDown={(e) => e.key === 'Enter' && commitRecord(e.currentTarget.value)}
          />
        </Labeled>
        <Labeled label={t('Filter_Action')} htmlFor={`${id}-action`}>
          <Select<string> id={`${id}-action`} clearable options={actions} value={pick(actions, params.action)} placeholder={t('Filter_AllActions')} onChange={(o) => onChange({ action: o?.value ?? '' })} />
        </Labeled>
      </div>

      {(params.request || hasFilters(params)) && (
        <div className={styles.chipsRow}>
          {params.request && (
            <span className={styles.chip}>
              <span>
                {t('Chip_Request')}: <bdi dir="ltr">{params.request}</bdi>
              </span>
              <button type="button" className={styles.chipRemove} aria-label={t('Chip_Remove')} onClick={() => onChange({ request: '' })}>
                ×
              </button>
            </span>
          )}
          <Button variant="ghost" onClick={onReset}>
            {t('Btn_Reset')}
          </Button>
        </div>
      )}
    </section>
  )
}
