import { api } from '../../lib/api'
import { t } from '../../lib/i18n'

// An upcoming appointment that a schedule change (a blocked date, or a preset that closes a day)
// would leave outside the opening hours -- from the backend's schedule/*/conflicts previews.
export type ScheduleConflict = { id: string; date: string; startTime: string; endTime: string; customerName: string; itemName: string }

type Ask = (message: string, options?: {
  yesLabel?: string; noLabel?: string; danger?: boolean
  checkbox?: { label: string; onChange: (checked: boolean) => void }
}) => Promise<boolean>

const MaxListed = 8

// QA run 4 OWN-1: blocking a date left its appointments booked with no warning. Lists them and
// asks before saving; the ticked-by-default checkbox cancels them (with the customer notice)
// once the change is saved. proceed=false means the owner went back.
export async function confirmScheduleConflicts(ask: Ask, lang: string, conflicts: ScheduleConflict[]) {
  if (conflicts.length === 0) return { proceed: true, cancel: false }
  let cancel = true
  const lines = conflicts.slice(0, MaxListed).map((c) => `• ${c.date} ${c.startTime}–${c.endTime} · ${c.customerName}`)
  if (conflicts.length > MaxListed) lines.push(t(lang, 'andMoreCount').replace('{count}', String(conflicts.length - MaxListed)))
  const proceed = await ask(`${t(lang, 'scheduleConflictsIntro').replace('{count}', String(conflicts.length))}\n${lines.join('\n')}`, {
    yesLabel: t(lang, 'saveAnyway'), noLabel: t(lang, 'goBack'), danger: false,
    checkbox: { label: t(lang, 'cancelConflictsAndNotify'), onChange: (checked) => { cancel = checked } },
  })
  return { proceed, cancel: proceed && cancel }
}

// Cancels through the owner's normal cancel (customer notice included). Returns how many failed.
export async function cancelConflicts(conflicts: ScheduleConflict[]) {
  let failed = 0
  for (const c of conflicts) {
    try {
      await api.patch(`/admin/appointments/${c.id}`, { status: 'CANCELLED', notifyCustomer: true })
    } catch {
      failed++
    }
  }
  return failed
}
