import { useState } from 'react'
import { format, addDays, parseISO } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import { api } from '../../lib/api'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { t, serviceName } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'
import CancelOptionsModal from './CancelOptionsModal'

type Appointment = {
  id: string; date: string; startTime: string; endTime: string
  status: string; notes: string | null
  customer: { name: string; phone: string }
  service: { nameEn: string; nameAr: string; nameHe: string; durationMinutes: number }
  price: number
  photoUrl: string | null
  recurringSeriesId: string | null
  pendingCancellationApproval: boolean
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
  const [showCancelOptions, setShowCancelOptions] = useState(false)
  const queryClient = useQueryClient()

  const { data: settings } = useQuery<{ waitlistEnabled: boolean }>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  const weekStartDate = parseISO(weekStart)
  const days = Array.from({ length: 7 }, (_, i) => addDays(weekStartDate, i))
  const dateLocale = lang === 'AR' ? ar : lang === 'HE' ? he : enUS
  const startMinute = 7 * 60
  const totalMinutes = 14 * 60

  function onCancelFlowDone() {
    setShowCancelOptions(false)
    setSelected(null)
    queryClient.invalidateQueries({ queryKey: ['dashboard'] })
  }

  return (
    <div>
      <div className="flex items-center justify-between mb-4">
        <button onClick={() => onWeekChange(weekOffset - 1)}
          className="text-gray-400 hover:text-white px-3 py-1.5 rounded-lg hover:bg-gray-800 transition-colors">
          {t(lang, 'previous')}
        </button>
        <span className="text-white font-medium">
          {format(weekStartDate, 'MMM d', { locale: dateLocale })} – {format(addDays(weekStartDate, 6), 'MMM d, yyyy', { locale: dateLocale })}
        </span>
        <button onClick={() => onWeekChange(weekOffset + 1)}
          className="text-gray-400 hover:text-white px-3 py-1.5 rounded-lg hover:bg-gray-800 transition-colors">
          {t(lang, 'next')}
        </button>
      </div>

      {/* Below md, 8 squished columns are unreadable, so the grid gets a fixed min-width and
          scrolls horizontally within its own box instead of shrinking illegibly -- avoids
          forcing the owner to rotate the phone to landscape just to read the board. */}
      <div className="bg-gray-900 rounded-2xl border border-gray-800 overflow-x-auto">
        <div className="min-w-[640px]">
          <div className="grid grid-cols-8 border-b border-gray-800">
            <div className="p-3" />
            {days.map((d) => (
              <div key={d.toISOString()} className="p-3 text-center border-s border-gray-800">
                <div className="text-xs text-gray-500 uppercase">{format(d, 'EEE', { locale: dateLocale })}</div>
                <div className={`text-lg font-semibold mt-0.5 ${
                  format(d, 'yyyy-MM-dd') === format(new Date(), 'yyyy-MM-dd') ? 'text-blue-400' : 'text-white'
                }`}>{format(d, 'd')}</div>
              </div>
            ))}
          </div>

          <div className="grid grid-cols-8 relative" style={{ height: `${totalMinutes * 1.2}px` }}>
            <div className="border-e border-gray-800">
              {HOURS.map((h) => (
                <div key={h} className="text-xs text-gray-600 text-end pe-2 absolute w-full"
                  style={{ top: `${(h * 60 - startMinute) * 1.2}px` }}>
                  {String(h).padStart(2, '0')}:00
                </div>
              ))}
            </div>
            {days.map((day) => {
              const dayStr = format(day, 'yyyy-MM-dd')
              const dayAppts = appointments.filter((a) => a.date.slice(0, 10) === dayStr)
              return (
                <div key={dayStr} className="relative border-s border-gray-800">
                  {HOURS.map((h) => (
                    <div key={h} className="absolute w-full border-t border-gray-800/50"
                      style={{ top: `${(h * 60 - startMinute) * 1.2}px`, height: `${60 * 1.2}px` }} />
                  ))}
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
                        className={`absolute inset-x-0.5 rounded-md px-1.5 py-1 text-left overflow-hidden ${color} hover:opacity-80 transition-opacity`}
                        style={{ top, height: Math.max(height, 24) }}>
                        {/* One line, not two — a short appointment's block (min 24px) only has
                            room for a single text-xs line; a second stacked line gets silently
                            clipped by overflow-hidden, hiding the service name entirely. */}
                        <div className="text-white text-xs font-medium truncate">
                          {appt.pendingCancellationApproval ? '⚠️ ' : appt.recurringSeriesId && '🔁 '}{appt.customer.name} · {serviceName(appt.service, lang)}
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

      {selected && (
        <div onClick={() => setSelected(null)} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
          <div onClick={(e) => e.stopPropagation()} className="bg-gray-900 rounded-2xl p-6 w-full max-w-sm border border-gray-800">
            <div className="relative mb-4">
              <div className="text-center px-11">
                <h2 className="text-white font-semibold text-lg">{selected.customer.name}</h2>
                {selected.pendingCancellationApproval && (
                  <div className="text-xs text-amber-400 mt-0.5">⚠️ {t(lang, 'cancellationRequestedBadge')}</div>
                )}
                {selected.recurringSeriesId && (
                  <div className="text-xs text-purple-400 mt-0.5">🔁 {t(lang, 'partOfSeries')}</div>
                )}
              </div>
              <button onClick={() => setSelected(null)}
                className="absolute top-1/2 -translate-y-1/2 end-0 w-11 h-11 flex items-center justify-center rounded-lg text-gray-500 hover:text-white hover:bg-gray-800 text-2xl leading-none transition-colors"
                aria-label="Close">✕</button>
            </div>
            <div className="space-y-2 text-sm mb-6">
              <Row label={t(lang, 'service')} value={serviceName(selected.service, lang)} />
              <Row label={t(lang, 'date')} value={selected.date.slice(0, 10)} />
              <Row label={t(lang, 'time')} value={`${selected.startTime} – ${selected.endTime}`} />
              <Row label={t(lang, 'phone')} value={selected.customer.phone} />
              {selected.notes && <Row label={t(lang, 'notes')} value={selected.notes} />}
              <Row label={t(lang, 'status')} value={selected.status}
                valueClass={selected.status === 'CONFIRMED' ? 'text-blue-400' : selected.status === 'COMPLETED' ? 'text-green-400' : 'text-gray-400'} />
            </div>
            {selected.photoUrl && (
              <div className="mb-6">
                <div className="text-gray-400 text-sm mb-2">{t(lang, 'referencePhoto')}</div>
                <a href={mediaUrl(selected.photoUrl)} target="_blank" rel="noreferrer">
                  <img src={mediaUrl(selected.photoUrl)} alt={t(lang, 'referencePhoto')}
                    className="w-full max-h-64 object-cover rounded-xl border border-gray-700 hover:border-blue-500 transition-colors" />
                </a>
              </div>
            )}
            {selected.status === 'CONFIRMED' && (
              <button onClick={() => setShowCancelOptions(true)}
                className={`w-full text-white text-sm font-medium py-2 rounded-lg transition-colors ${
                  selected.pendingCancellationApproval ? 'bg-amber-600 hover:bg-amber-500' : 'bg-gray-700 hover:bg-gray-600'
                }`}>
                {selected.pendingCancellationApproval ? t(lang, 'resolveCancellationRequest') : t(lang, 'cancel')}
              </button>
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
    </div>
  )
}

function Row({ label, value, valueClass = 'text-white' }: { label: string; value: string; valueClass?: string }) {
  return (
    <div className="flex justify-between">
      <span className="text-gray-400">{label}</span>
      <span className={valueClass}>{value}</span>
    </div>
  )
}
