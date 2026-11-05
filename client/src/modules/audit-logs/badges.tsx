import { useTranslation } from 'react-i18next'
import { StatusBadge } from '@/ui/StatusBadge'

/** Literal keys so the i18n lint sees them. */
const CATEGORY_KEYS: Record<string, string> = {
  Data: 'Cat_Data',
  Request: 'Cat_Request',
  Security: 'Cat_Security',
  Business: 'Cat_Business',
  System: 'Cat_System',
  Diagnostic: 'Cat_Diagnostic',
}
const AREA_KEYS: Record<string, string> = {
  Sales: 'Area_Sales',
  Pos: 'Area_Pos',
  Inventory: 'Area_Inventory',
  Customers: 'Area_Customers',
  Account: 'Area_Account',
  Settings: 'Area_Settings',
  Reports: 'Area_Reports',
  System: 'Area_System',
  Other: 'Area_Other',
}
const OUTCOME_KEYS: Record<string, string> = { Success: 'Out_Success', Failure: 'Out_Failure', Denied: 'Out_Denied' }
const SEVERITY_KEYS: Record<string, string> = { Info: 'Sev_Info', Warning: 'Sev_Warning', Error: 'Sev_Error', Critical: 'Sev_Critical' }

export const categoryKey = (category: string) => CATEGORY_KEYS[category]
export const areaKey = (area: string) => AREA_KEYS[area]
export const outcomeKey = (outcome: string) => OUTCOME_KEYS[outcome]
export const severityKey = (severity: string) => SEVERITY_KEYS[severity]

/** Neutral hues for the kinds of event; green / amber / red are kept for real outcomes. */
const CATEGORY_TONES: Record<string, 'neutral' | 'info' | 'ink'> = {
  Data: 'info',
  Request: 'neutral',
  Security: 'ink',
  Business: 'info',
  System: 'neutral',
  Diagnostic: 'neutral',
}
const OUTCOME_TONES: Record<string, 'success' | 'danger' | 'warning'> = { Success: 'success', Failure: 'danger', Denied: 'warning' }

export function CategoryBadge({ category }: { category: string }) {
  const { t } = useTranslation('auditLogs')
  return <StatusBadge tone={CATEGORY_TONES[category] ?? 'neutral'} label={categoryKey(category) ? t(categoryKey(category) as string) : category} />
}

/** Which part of the product an entry belongs to (sales, the till, stock ...). Plain text: the colours are kept for outcomes. */
export function AreaBadge({ area }: { area: string }) {
  const { t } = useTranslation('auditLogs')
  return <StatusBadge tone="neutral" label={areaKey(area) ? t(areaKey(area) as string) : area} />
}

/** The result, plus the severity when it is worse than routine. */
export function OutcomeBadges({ outcome, severity }: { outcome: string; severity: string }) {
  const { t } = useTranslation('auditLogs')
  const loud = severity === 'Warning' || severity === 'Error' || severity === 'Critical'
  return (
    <>
      <StatusBadge tone={OUTCOME_TONES[outcome] ?? 'neutral'} label={outcomeKey(outcome) ? t(outcomeKey(outcome) as string) : outcome} />
      {loud && (
        <>
          {' '}
          <StatusBadge tone={severity === 'Warning' ? 'warning' : 'danger'} filled={severity === 'Critical'} label={t(severityKey(severity) as string)} />
        </>
      )}
    </>
  )
}

const pad = (n: number) => String(n).padStart(2, '0')

/**
 * Stored as UTC, shown in the computer's own time zone in an unambiguous year-first format in both languages
 * (`2026-10-24 00:01:48`), as the Razor page did. Callers put it in a `dir="ltr"` element so Arabic does not reorder it.
 */
export function formatStamp(iso: string): string {
  const d = new Date(iso)
  if (Number.isNaN(d.getTime())) return iso
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

/** A `Date` as the value of `<input type="datetime-local">` (local time, minute precision). */
export function toLocalInput(d: Date): string {
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`
}
