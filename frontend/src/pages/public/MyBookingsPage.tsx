import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import { useAuth } from '../../lib/auth'
import { customerApi } from '../../lib/customerApi'
import { t, serviceName, type TKey } from '../../lib/i18n'
import CustomerAccountNav from '../../components/customer/CustomerAccountNav'

type Appointment = {
  id: string; barberSlug: string; barberName: string
  date: string; startTime: string; endTime: string
  notes: string | null; status: string; cancelToken: string
  service: { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number }
}

type Slot = { start: string; end: string }

const FILTERS: { value: string; key: TKey }[] = [
  { value: 'upcoming', key: 'upcoming' },
  { value: 'today', key: 'today' },
  { value: 'past', key: 'past' },
  { value: 'all', key: 'all' },
]
const STATUS_COLORS: Record<string, string> = {
  CONFIRMED: 'bg-blue-900/50 text-blue-300 border-blue-800/50',
  COMPLETED: 'bg-green-900/50 text-green-300 border-green-800/50',
  CANCELLED: 'bg-gray-800/50 text-gray-500 border-gray-700/50',
}

export default function MyBookingsPage() {
  const { language: lang } = useAuth()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState('upcoming')
  const [expandedReschedule, setExpandedReschedule] = useState<string | null>(null)
  const [expandedNotes, setExpandedNotes] = useState<string | null>(null)
  const [noteDraft, setNoteDraft] = useState('')
  const [rescheduleDate, setRescheduleDate] = useState('')
  const [busy, setBusy] = useState<string | null>(null)

  const { data: appointments = [], isLoading } = useQuery<Appointment[]>({
    queryKey: ['my-bookings', filter],
    queryFn: () => customerApi.get(`/customer/appointments?filter=${filter}`).then((r) => r.data),
  })

  const { data: slots = [], isFetching: loadingSlots } = useQuery<Slot[]>({
    queryKey: ['reschedule-slots', expandedReschedule, rescheduleDate],
    enabled: !!expandedReschedule && !!rescheduleDate,
    queryFn: () => {
      const appt = appointments.find((a) => a.id === expandedReschedule)!
      return customerApi
        .get(`/${appt.barberSlug}/availability?date=${rescheduleDate}&serviceId=${appt.service.id}`)
        .then((r) => r.data.slots)
    },
  })

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ['my-bookings'] })
  }

  async function cancelAppointment(id: string) {
    if (!confirm(t(lang, 'cancelConfirm'))) return
    setBusy(id)
    try {
      await customerApi.post(`/customer/appointments/${id}/cancel`)
      invalidate()
    } finally { setBusy(null) }
  }

  async function confirmReschedule(id: string, startTime: string) {
    setBusy(id)
    try {
      await customerApi.patch(`/customer/appointments/${id}/reschedule`, { date: rescheduleDate, startTime })
      setExpandedReschedule(null)
      setRescheduleDate('')
      invalidate()
    } finally { setBusy(null) }
  }

  async function saveNote(id: string) {
    setBusy(id)
    try {
      await customerApi.patch(`/customer/appointments/${id}/notes`, { notes: noteDraft })
      setExpandedNotes(null)
      invalidate()
    } finally { setBusy(null) }
  }

  const emptyKey: TKey = filter === 'past' ? 'noPastBookings' : 'noUpcomingBookings'

  return (
    <div className="min-h-screen bg-gray-950 text-white">
      <CustomerAccountNav />
      <div className="max-w-2xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold text-white mb-6">{t(lang, 'myBookingsTitle')}</h1>

        <div className="flex gap-1 bg-gray-900 rounded-xl p-1 border border-gray-800 mb-6 w-fit">
          {FILTERS.map((f) => (
            <button key={f.value} onClick={() => setFilter(f.value)}
              className={`px-4 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                filter === f.value ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white'
              }`}>{t(lang, f.key)}
            </button>
          ))}
        </div>

        {isLoading ? (
          <div className="text-center text-gray-500 py-12">{t(lang, 'loading')}</div>
        ) : appointments.length === 0 ? (
          <div className="text-center text-gray-500 py-12">{t(lang, emptyKey)}</div>
        ) : (
          <div className="space-y-3">
            {appointments.map((appt) => (
              <div key={appt.id}
                className={`bg-gray-900 border rounded-2xl p-4 ${appt.status !== 'CONFIRMED' ? 'border-gray-800 opacity-60' : 'border-gray-700'}`}>
                <div className="flex items-start justify-between gap-3">
                  <div className="flex-1 min-w-0">
                    <div className="font-semibold text-white truncate">{appt.barberName}</div>
                    <div className="text-gray-400 text-sm mt-1">{serviceName(appt.service, lang)}</div>
                    <div className="text-gray-500 text-sm mt-1">
                      {format(parseISO(appt.date), 'EEE, MMM d yyyy', { locale: lang === 'AR' ? ar : lang === 'HE' ? he : enUS })}
                      {' · '}{appt.startTime}–{appt.endTime}
                    </div>
                    {appt.notes && expandedNotes !== appt.id && (
                      <div className="text-gray-500 text-sm mt-1 italic">"{appt.notes}"</div>
                    )}
                  </div>
                  <span className={`shrink-0 text-xs font-medium px-2.5 py-1 rounded-full border ${STATUS_COLORS[appt.status] ?? STATUS_COLORS.CANCELLED}`}>
                    {t(lang, appt.status === 'CONFIRMED' ? 'statusConfirmed' : appt.status === 'COMPLETED' ? 'statusCompleted' : 'statusCancelled')}
                  </span>
                </div>

                {appt.status === 'CONFIRMED' && (
                  <div className="mt-3 flex flex-wrap gap-2">
                    <button
                      onClick={() => { setExpandedReschedule(expandedReschedule === appt.id ? null : appt.id); setRescheduleDate('') }}
                      className="flex-1 border border-gray-700 text-gray-300 hover:bg-gray-800 text-sm font-medium py-2 rounded-xl transition-colors">
                      {t(lang, 'rescheduleAppointment')}
                    </button>
                    <button
                      onClick={() => { setExpandedNotes(expandedNotes === appt.id ? null : appt.id); setNoteDraft(appt.notes ?? '') }}
                      className="flex-1 border border-gray-700 text-gray-300 hover:bg-gray-800 text-sm font-medium py-2 rounded-xl transition-colors">
                      {appt.notes ? t(lang, 'editNote') : t(lang, 'addNote')}
                    </button>
                    <button
                      disabled={busy === appt.id}
                      onClick={() => cancelAppointment(appt.id)}
                      className="flex-1 border border-red-800/60 text-red-400 hover:bg-red-900/30 text-sm font-medium py-2 rounded-xl transition-colors disabled:opacity-50">
                      {t(lang, 'cancelAppointment')}
                    </button>
                  </div>
                )}

                {expandedNotes === appt.id && (
                  <div className="mt-3 space-y-2">
                    <textarea
                      value={noteDraft} onChange={(e) => setNoteDraft(e.target.value)}
                      rows={2}
                      className="w-full bg-gray-800 border border-gray-700 rounded-xl px-3 py-2 text-white text-sm placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
                    />
                    <button
                      disabled={busy === appt.id}
                      onClick={() => saveNote(appt.id)}
                      className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-semibold py-2 rounded-xl transition-colors">
                      {t(lang, 'saveNote')}
                    </button>
                  </div>
                )}

                {expandedReschedule === appt.id && (
                  <div className="mt-3 space-y-2">
                    <p className="text-gray-400 text-sm">{t(lang, 'selectNewTime')}</p>
                    <input
                      type="date" value={rescheduleDate}
                      min={new Date().toISOString().slice(0, 10)}
                      onChange={(e) => setRescheduleDate(e.target.value)}
                      className="w-full bg-gray-800 border border-gray-700 rounded-xl px-3 py-2 text-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
                    />
                    {rescheduleDate && (
                      loadingSlots ? (
                        <div className="text-center text-gray-500 text-sm py-3">{t(lang, 'loadingTimes')}</div>
                      ) : slots.length === 0 ? (
                        <div className="text-center text-gray-500 text-sm py-3">{t(lang, 'noTimes')}</div>
                      ) : (
                        <div className="grid grid-cols-4 gap-2">
                          {slots.map((s) => (
                            <button
                              key={s.start}
                              disabled={busy === appt.id}
                              onClick={() => confirmReschedule(appt.id, s.start)}
                              className="bg-gray-800 hover:bg-blue-600 border border-gray-700 text-white text-sm py-2 rounded-lg transition-colors disabled:opacity-50">
                              {s.start}
                            </button>
                          ))}
                        </div>
                      )
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
