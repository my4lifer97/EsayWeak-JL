import { useState, type CSSProperties } from 'react'
import { Link } from 'react-router-dom'
import { format, addDays, parseISO, type Locale } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import { api } from '../../lib/api'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { t, itemName } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'
import { presetForDate } from '../../lib/effectiveSchedule'
import type { PresetSchedule, SchedulePreset } from './PresetEditorModal'
import CancelOptionsModal from './CancelOptionsModal'
import RescheduleModal from './RescheduleModal'

type Appointment = {
  id: string; date: string; startTime: string; endTime: string
  status: string; notes: string | null
  customer: { name: string; phone: string }
  item: { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number | null }
  price: number
  photoUrl: string | null
  recurringSeriesId: string | null
  pendingCancellationApproval: boolean
}

type WorkingHour = { dayOfWeek: number; startTime: string; endTime: string; isActive: boolean }
type Break = { id: string; dayOfWeek: number; startTime: string; endTime: string }
type BlockedSlot = { id: string; date: string; startTime: string | null; endTime: string | null; reason: string | null }
type Schedule = { workingHours: WorkingHour[]; breaks: Break[]; blockedSlots: BlockedSlot[] }

// What the owner clicked on when it isn't an appointment -- drives the info popup.
type ScheduleSelection =
  | { kind: 'closed'; date: Date }
  | { kind: 'break'; date: Date; startTime: string; endTime: string }
  | { kind: 'blocked'; slot: BlockedSlot; rangeStart: Date; rangeEnd: Date }

// Diagonal stripes mark time customers can't book (closed / blocked), so it reads as
// "unavailable" at a glance and never gets mistaken for a solid appointment block.
const hatch = (rgba: string) => ({
  backgroundImage: `repeating-linear-gradient(135deg, ${rgba} 0 6px, transparent 6px 12px)`,
})
const CLOSED_STYLE = { className: 'bg-stone-400/15 text-muted', style: hatch('rgba(120,113,108,0.18)') }
const BLOCKED_STYLE = { className: 'bg-rose-500/10 border border-rose-400/60 text-rose-700 dark:text-rose-300', style: hatch('rgba(244,63,94,0.14)') }
const BREAK_CLASS = 'bg-teal/15 border border-teal/50 text-teal'

// A blocked range is stored as one BlockedSlot per day, so rebuild the range the clicked day
// belongs to: consecutive days sharing the same times and reason.
function blockedRange(slot: BlockedSlot, all: BlockedSlot[]) {
  const key = (b: BlockedSlot) => `${b.startTime}|${b.endTime}|${b.reason ?? ''}`
  const dates = new Set(all.filter((b) => key(b) === key(slot)).map((b) => b.date.slice(0, 10)))
  let start = parseISO(slot.date.slice(0, 10))
  let end = start
  while (dates.has(format(addDays(start, -1), 'yyyy-MM-dd'))) start = addDays(start, -1)
  while (dates.has(format(addDays(end, 1), 'yyyy-MM-dd'))) end = addDays(end, 1)
  return { rangeStart: start, rangeEnd: end }
}

const STATUS_COLORS: Record<string, string> = {
  CONFIRMED: 'bg-blue-600',
  COMPLETED: 'bg-green-700',
  CANCELLED: 'bg-gray-700',
}

const HOURS = Array.from({ length: 14 }, (_, i) => i + 7)

function timeToMinutes(t: string) {
  const [h, m] = t.split(':').map(Number)
  return h * 60 + m
}

export default function WeeklyCalendar({
  appointments, weekStart, weekOffset, onWeekChange, lang,
}: {
  appointments: Appointment[]; weekStart: string; weekOffset: number
  onWeekChange: (offset: number) => void; lang: string
}) {
  const [selected, setSelected] = useState<Appointment | null>(null)
  const [scheduleSel, setScheduleSel] = useState<ScheduleSelection | null>(null)
  const [showCancelOptions, setShowCancelOptions] = useState(false)
  const [showReschedule, setShowReschedule] = useState(false)
  const queryClient = useQueryClient()

  const { data: settings } = useQuery<{ waitlistEnabled: boolean }>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  // Same query key as the Schedule page, so its data is shared and refetched on every visit.
  const { data: schedule } = useQuery<Schedule>({
    queryKey: ['schedule'],
    queryFn: () => api.get('/admin/schedule').then((r) => r.data),
  })

  // Same query keys as the Schedule page -- lets each column use the hours of whatever preset
  // actually applies on that date (a scheduled range), not just the live weekly template.
  const { data: presets = [] } = useQuery<SchedulePreset[]>({
    queryKey: ['schedule-presets'],
    queryFn: () => api.get('/admin/schedule/presets').then((r) => r.data),
  })
  const { data: scheduledChange } = useQuery<PresetSchedule | null>({
    queryKey: ['preset-schedule'],
    queryFn: () => api.get('/admin/schedule/preset-schedule').then((r) => r.data.schedule),
  })

  const weekStartDate = parseISO(weekStart)
  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStartDate, i))
  const dateLocale = lang === 'AR' ? ar : lang === 'HE' ? he : enUS
  const startMinute = 7 * 60
  const totalMinutes = 14 * 60
  const endMinute = startMinute + totalMinutes

  // Clamps a time range to the visible 07:00-21:00 window; null if it falls entirely outside.
  function span(start: string, end: string) {
    const s = Math.max(timeToMinutes(start), startMinute)
    const e = Math.min(timeToMinutes(end), endMinute)
    return e <= s ? null : { top: (s - startMinute) * 1.2, height: (e - s) * 1.2 }
  }

  function dayInfo(day: Date) {
    const dayStr = format(day, 'yyyy-MM-dd')
    const match = presetForDate(dayStr, scheduledChange, presets)
    const hours = match
      ? match.preset.days.find((h) => h.dayOfWeek === day.getDay())
      : schedule?.workingHours.find((h) => h.dayOfWeek === day.getDay())
    // Only call a day closed once the schedule has loaded and has hours configured --
    // otherwise every column would flash grey while the request is in flight.
    const closed = !!schedule && schedule.workingHours.length > 0 && (!hours || !hours.isActive)
    const blockedToday = schedule?.blockedSlots.filter((b) => b.date.slice(0, 10) === dayStr) ?? []
    const fullDayBlock = blockedToday.find((b) => !b.startTime)
    const partialBlocks = blockedToday.filter((b) => b.startTime && b.endTime)
    const breaks: Break[] = match
      ? match.preset.breaks.filter((b) => b.dayOfWeek === day.getDay()).map((b, i) => ({ ...b, id: `${match.preset.id}-${i}` }))
      : schedule?.breaks.filter((b) => b.dayOfWeek === day.getDay()) ?? []
    // Name of the scheduled preset covering this date (not the Default it reverts to afterward).
    const presetName = match?.inRange ? match.preset.name : null
    return { dayStr, hours, closed, fullDayBlock, partialBlocks, breaks, presetName }
  }

  function selectBlocked(slot: BlockedSlot) {
    setScheduleSel({ kind: 'blocked', slot, ...blockedRange(slot, schedule?.blockedSlots ?? []) })
  }

  function onCancelFlowDone() {
    setShowCancelOptions(false)
    setSelected(null)
    queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }

  function onRescheduleDone() {
    setShowReschedule(false)
    setSelected(null)
    queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <button onClick={() => onWeekChange(weekOffset - 1)}
          className="text-muted hover:text-ink px-3 py-1.5 rounded-lg hover:bg-cream transition-colors">
          {t(lang, 'previous')}
        </button>
        <span className="text-ink font-medium">
          {format(weekStartDate, 'MMM d', { locale: dateLocale })} – {format(addDays(weekStartDate, 6), 'MMM d, yyyy', { locale: dateLocale })}
        </span>
        <button onClick={() => onWeekChange(weekOffset + 1)}
          className="text-muted hover:text-ink px-3 py-1.5 rounded-lg hover:bg-cream transition-colors">
          {t(lang, 'next')}
        </button>
      </div>

      {/* Below md, 8 squished columns are unreadable, so the grid gets a fixed min-width and
          scrolls horizontally within its own box instead of shrinking illegibly -- avoids
          forcing the owner to rotate the phone to landscape just to read the board. */}
      <div className="bg-surface rounded-2xl border border-line overflow-x-auto">
        {/* Forced ltr: the hour-labels column is first in DOM/grid-track order, and a CSS grid
            places its first track at the inline-start edge -- in an rtl document that's the
            right side, pushing the time axis to the right instead of the left. Locking this
            subtree to ltr keeps track 1 (hours) pinned left regardless of app language; the
            Hebrew/Arabic day names inside still render correctly since per-character bidi
            shaping is independent of the container's `dir`. */}
        <div className="min-w-[640px]" dir="ltr">
          <div className="grid grid-cols-8 border-b border-line">
            <div className="p-3" />
            {days.map((d) => {
              const { closed, fullDayBlock, presetName } = dayInfo(d)
              const off = closed || !!fullDayBlock
              return (
                <div key={d.toISOString()} className={`p-3 text-center border-s border-line ${off ? 'bg-stone-400/10' : ''}`}>
                  <div className="text-xs text-muted uppercase">{format(d, 'EEE', { locale: dateLocale })}</div>
                  <div className={`text-lg font-semibold mt-0.5 ${
                    format(d, 'yyyy-MM-dd') === format(new Date(), 'yyyy-MM-dd') ? 'text-coral-dark' : off ? 'text-muted' : 'text-ink'
                  }`}>{format(d, 'd')}</div>
                  {fullDayBlock ? (
                    <div className="text-[10px] font-medium text-rose-600 dark:text-rose-400 truncate">{t(lang, 'calBlocked')}</div>
                  ) : closed ? (
                    <div className="text-[10px] font-medium text-muted truncate">{t(lang, 'dayClosedLabel')}</div>
                  ) : null}
                  {presetName && (
                    <div className="mt-1 inline-block max-w-full text-[10px] font-medium bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400 px-1.5 py-0.5 rounded truncate" title={presetName}>
                      {presetName}
                    </div>
                  )}
                </div>
              )
            })}
          </div>

          <div className="grid grid-cols-8 relative" style={{ height: `${totalMinutes * 1.2}px` }}>
            <div className="relative border-e border-line">
              {HOURS.map((h) => (
                <div key={h} className="text-xs text-muted text-end pe-2 absolute w-full"
                  style={{ top: `${(h * 60 - startMinute) * 1.2}px` }}>
                  {String(h).padStart(2, '0')}:00
                </div>
              ))}
            </div>
            {days.map((day) => {
              const { dayStr, hours, closed, fullDayBlock, partialBlocks, breaks } = dayInfo(day)
              const dayAppts = appointments.filter((a) => a.date.slice(0, 10) === dayStr)
              const open = hours && !closed && !fullDayBlock
              const beforeOpen = open ? span('00:00', hours.startTime) : null
              const afterClose = open ? span(hours.endTime, '23:59') : null
              return (
                <div key={dayStr} className="relative border-s border-line">
                  {HOURS.map((h) => (
                    <div key={h} className="absolute w-full border-t border-line/60"
                      style={{ top: `${(h * 60 - startMinute) * 1.2}px`, height: `${60 * 1.2}px` }} />
                  ))}
                  {/* Schedule layers sit under the appointments (z-10), so an appointment booked
                      before a day got closed/blocked is still visible and clickable. */}
                  {beforeOpen && <div className="absolute inset-x-0 bg-stone-400/10" style={beforeOpen} title={t(lang, 'calOutsideHours')} />}
                  {afterClose && <div className="absolute inset-x-0 bg-stone-400/10" style={afterClose} title={t(lang, 'calOutsideHours')} />}
                  {fullDayBlock ? (
                    <button onClick={() => selectBlocked(fullDayBlock)}
                      className={`absolute inset-0.5 rounded-md p-1.5 text-start hover:opacity-80 transition-opacity ${BLOCKED_STYLE.className}`}
                      style={BLOCKED_STYLE.style}>
                      <div className="text-xs font-semibold truncate">⛔ {t(lang, 'calBlocked')}</div>
                      {fullDayBlock.reason && <div className="text-[11px] truncate">{fullDayBlock.reason}</div>}
                    </button>
                  ) : closed ? (
                    <button onClick={() => setScheduleSel({ kind: 'closed', date: day })}
                      className={`absolute inset-0.5 rounded-md p-1.5 text-start hover:opacity-80 transition-opacity ${CLOSED_STYLE.className}`}
                      style={CLOSED_STYLE.style}>
                      <div className="text-xs font-semibold truncate">🔒 {t(lang, 'dayClosedLabel')}</div>
                    </button>
                  ) : (
                    <>
                      {breaks.map((br) => {
                        const pos = span(br.startTime, br.endTime)
                        return pos && (
                          <button key={br.id} onClick={() => setScheduleSel({ kind: 'break', date: day, startTime: br.startTime, endTime: br.endTime })}
                            className={`absolute inset-x-0.5 rounded-md px-1.5 py-0.5 text-start overflow-hidden hover:opacity-80 transition-opacity ${BREAK_CLASS}`}
                            style={{ top: pos.top, height: Math.max(pos.height, 20) }}>
                            <div className="text-xs font-medium truncate">☕ {t(lang, 'calBreak')}</div>
                          </button>
                        )
                      })}
                      {partialBlocks.map((b) => {
                        const pos = span(b.startTime!, b.endTime!)
                        return pos && (
                          <button key={b.id} onClick={() => selectBlocked(b)}
                            className={`absolute inset-x-0.5 rounded-md px-1.5 py-0.5 text-start overflow-hidden hover:opacity-80 transition-opacity ${BLOCKED_STYLE.className}`}
                            style={{ ...BLOCKED_STYLE.style, top: pos.top, height: Math.max(pos.height, 20) }}>
                            <div className="text-xs font-medium truncate">⛔ {b.reason || t(lang, 'calBlocked')}</div>
                          </button>
                        )
                      })}
                    </>
                  )}
                  {dayAppts.map((appt) => {
                    const top = (timeToMinutes(appt.startTime) - startMinute) * 1.2
                    const height = (timeToMinutes(appt.endTime) - timeToMinutes(appt.startTime)) * 1.2
                    // A pending cancellation-approval request takes priority over both the
                    // recurring-purple and normal status colors -- it needs the owner's attention
                    // above anything else on the board. Recurring appointments get their own color
                    // (instead of the default confirmed-blue) so they stand out as "reserved every
                    // week" at a glance -- completed/cancelled still show their own status color.
                    const color = appt.pendingCancellationApproval
                      ? 'bg-amber-500'
                      : appt.recurringSeriesId && appt.status === 'CONFIRMED'
                      ? 'bg-purple-600'
                      : STATUS_COLORS[appt.status] ?? 'bg-blue-600'
                    return (
                      <button key={appt.id} onClick={() => setSelected(appt)}
                        title={appt.pendingCancellationApproval ? t(lang, 'cancellationRequestedBadge') : appt.recurringSeriesId ? t(lang, 'partOfSeries') : undefined}
                        className={`absolute inset-x-0.5 z-10 rounded-md px-1.5 py-1 text-left overflow-hidden ${color} hover:opacity-80 transition-opacity`}
                        style={{ top, height: Math.max(height, 24) }}>
                        {/* One line, not two — a short appointment's block (min 24px) only has
                            room for a single text-xs line; a second stacked line gets silently
                            clipped by overflow-hidden, hiding the service name entirely. */}
                        <div className="text-white text-xs font-medium truncate">
                          {appt.pendingCancellationApproval ? '⚠️ ' : appt.recurringSeriesId && '🔁 '}{appt.customer.name} · {itemName(appt.item, lang)}
                        </div>
                      </button>
                    )
                  })}
                </div>
              )
            })}
          </div>
        </div>
      </div>

      <div className="flex flex-wrap gap-x-4 gap-y-2 mt-3 text-xs text-muted">
        <LegendSwatch className="bg-blue-600" label={t(lang, 'calLegendAppointment')} />
        <LegendSwatch className="bg-purple-600" label={t(lang, 'calLegendRecurring')} />
        <LegendSwatch className="bg-amber-500" label={t(lang, 'calLegendCancelRequest')} />
        <LegendSwatch className="bg-green-700" label={t(lang, 'calLegendCompleted')} />
        <LegendSwatch className={BREAK_CLASS} label={t(lang, 'calBreak')} />
        <LegendSwatch className={BLOCKED_STYLE.className} style={BLOCKED_STYLE.style} label={t(lang, 'calBlocked')} />
        <LegendSwatch className={CLOSED_STYLE.className} style={CLOSED_STYLE.style} label={t(lang, 'dayClosedLabel')} />
      </div>

      {scheduleSel && (
        <ScheduleInfoModal sel={scheduleSel} lang={lang} dateLocale={dateLocale} onClose={() => setScheduleSel(null)} />
      )}

      {selected && (
        <div onClick={() => setSelected(null)} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
          <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 w-full max-w-sm border border-line">
            <div className="relative mb-4">
              <div className="text-center px-11">
                <h2 className="text-ink font-semibold text-lg">{selected.customer.name}</h2>
                {selected.pendingCancellationApproval && (
                  <div className="text-xs text-amber-700 dark:text-amber-400 mt-0.5">⚠️ {t(lang, 'cancellationRequestedBadge')}</div>
                )}
                {selected.recurringSeriesId && (
                  <div className="text-xs text-purple-700 dark:text-purple-400 mt-0.5">🔁 {t(lang, 'partOfSeries')}</div>
                )}
              </div>
              <button onClick={() => setSelected(null)}
                className="absolute top-1/2 -translate-y-1/2 end-0 w-11 h-11 flex items-center justify-center rounded-lg text-muted hover:text-ink hover:bg-cream text-2xl leading-none transition-colors"
                aria-label="Close">✕</button>
            </div>
            <div className="space-y-2 text-sm mb-6">
              <Row label={t(lang, 'service')} value={itemName(selected.item, lang)} />
              <Row label={t(lang, 'date')} value={selected.date.slice(0, 10)} />
              <Row label={t(lang, 'time')} value={`${selected.startTime} – ${selected.endTime}`} />
              <Row label={t(lang, 'phone')} value={selected.customer.phone} />
              {selected.notes && <Row label={t(lang, 'notes')} value={selected.notes} />}
              <Row label={t(lang, 'status')} value={selected.status}
                valueClass={selected.status === 'CONFIRMED' ? 'text-blue-700 dark:text-blue-400' : selected.status === 'COMPLETED' ? 'text-green-700 dark:text-green-400' : 'text-muted'} />
            </div>
            {selected.photoUrl && (
              <div className="mb-6">
                <div className="text-muted text-sm mb-2">{t(lang, 'referencePhoto')}</div>
                <a href={mediaUrl(selected.photoUrl)} target="_blank" rel="noreferrer">
                  <img src={mediaUrl(selected.photoUrl)} alt={t(lang, 'referencePhoto')}
                    className="w-full max-h-64 object-cover rounded-xl border border-line hover:border-coral transition-colors" />
                </a>
              </div>
            )}
            {selected.status === 'CONFIRMED' && (
              <div className="space-y-2">
                {!selected.pendingCancellationApproval && (
                  <button onClick={() => setShowReschedule(true)}
                    className="w-full border border-line text-ink hover:bg-cream text-sm font-medium py-2 rounded-lg transition-colors">
                    {t(lang, 'rescheduleAppointment')}
                  </button>
                )}
                <button onClick={() => setShowCancelOptions(true)}
                  className={`w-full text-white text-sm font-medium py-2 rounded-lg transition-colors ${
                    selected.pendingCancellationApproval ? 'bg-amber-600 hover:bg-amber-500' : 'bg-teal hover:bg-teal/80'
                  }`}>
                  {selected.pendingCancellationApproval ? t(lang, 'resolveCancellationRequest') : t(lang, 'cancel')}
                </button>
              </div>
            )}
          </div>
        </div>
      )}

      {selected && showCancelOptions && (
        <CancelOptionsModal
          lang={lang}
          appointmentId={selected.id}
          waitlistEnabled={settings?.waitlistEnabled ?? false}
          onClose={() => setShowCancelOptions(false)}
          onDone={onCancelFlowDone}
        />
      )}

      {selected && showReschedule && (
        <RescheduleModal
          lang={lang}
          appointmentId={selected.id}
          itemId={selected.item.id}
          onClose={() => setShowReschedule(false)}
          onDone={onRescheduleDone}
        />
      )}
    </div>
  )
}

function LegendSwatch({ className, style, label }: { className: string; style?: CSSProperties; label: string }) {
  return (
    <span className="flex items-center gap-1.5">
      <span className={`inline-block w-3.5 h-3.5 rounded ${className}`} style={style} />
      {label}
    </span>
  )
}

function ScheduleInfoModal({ sel, lang, dateLocale, onClose }: {
  sel: ScheduleSelection; lang: string; dateLocale: Locale; onClose: () => void
}) {
  const fmt = (d: Date) => format(d, 'EEEE, MMM d', { locale: dateLocale })
  const view = sel.kind === 'closed'
    ? { icon: '🔒', title: t(lang, 'calClosedTitle'), accent: 'text-muted', body: t(lang, 'calClosedBody') }
    : sel.kind === 'break'
    ? { icon: '☕', title: t(lang, 'calBreak'), accent: 'text-teal', body: t(lang, 'calBreakBody') }
    : { icon: '⛔', title: t(lang, 'calBlocked'), accent: 'text-rose-600 dark:text-rose-400', body: t(lang, 'calBlockedBody') }

  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 w-full max-w-sm border border-line">
        <div className="relative mb-4">
          <div className="text-center px-11">
            <h2 className={`font-semibold text-lg ${view.accent}`}>{view.icon} {view.title}</h2>
          </div>
          <button onClick={onClose}
            className="absolute top-1/2 -translate-y-1/2 end-0 w-11 h-11 flex items-center justify-center rounded-lg text-muted hover:text-ink hover:bg-cream text-2xl leading-none transition-colors"
            aria-label="Close">✕</button>
        </div>
        <div className="space-y-2 text-sm mb-4">
          {sel.kind === 'blocked' ? (
            <>
              <Row label={t(lang, 'date')} value={sel.rangeStart.getTime() === sel.rangeEnd.getTime()
                ? fmt(sel.rangeStart)
                : `${fmt(sel.rangeStart)} ${t(lang, 'throughDate')} ${fmt(sel.rangeEnd)}`} />
              <Row label={t(lang, 'time')} value={sel.slot.startTime ? `${sel.slot.startTime} – ${sel.slot.endTime}` : t(lang, 'fullDay')} />
              {sel.slot.reason && <Row label={t(lang, 'calReason')} value={sel.slot.reason} />}
            </>
          ) : (
            <>
              <Row label={t(lang, 'date')} value={fmt(sel.date)} />
              {sel.kind === 'break' && <Row label={t(lang, 'time')} value={`${sel.startTime} – ${sel.endTime}`} />}
            </>
          )}
        </div>
        <p className="text-muted text-sm mb-6">{view.body}</p>
        <Link to="/admin/schedule"
          className="block w-full text-center border border-line text-ink hover:bg-cream text-sm font-medium py-2 rounded-lg transition-colors">
          {t(lang, 'calEditInSchedule')}
        </Link>
      </div>
    </div>
  )
}

function Row({ label, value, valueClass = 'text-ink' }: { label: string; value: string; valueClass?: string }) {
  return (
    <div className="flex justify-between">
      <span className="text-muted">{label}</span>
      <span className={valueClass}>{value}</span>
    </div>
  )
}
