import { useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t, itemName } from '../../lib/i18n'
import BackButton from '../../components/BackButton'

type AppointmentDetail = {
  id: string; date: string; startTime: string; endTime: string; status: string; notes: string | null; cancelToken: string
  item: { id: string; nameEn: string; nameAr: string; nameHe: string; isBookable: boolean }
  customer: { name: string; phone: string }
  business: { name: string; language: string }
}

type Slot = { start: string; end: string }

export default function AppointmentPage() {
  const { slug, id } = useParams<{ slug: string; id: string }>()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''
  const [loading, setLoading] = useState(false)
  const [cancelled, setCancelled] = useState(false)
  const [showReschedule, setShowReschedule] = useState(false)
  const [rescheduleDate, setRescheduleDate] = useState('')
  const [rescheduleError, setRescheduleError] = useState('')
  const [rescheduling, setRescheduling] = useState(false)
  const [justRescheduled, setJustRescheduled] = useState(false)

  const { data: appt, isLoading, refetch } = useQuery<AppointmentDetail>({
    queryKey: ['appointment', id],
    queryFn: () => api.get(`/${slug}/appointments/${id}`).then((r) => r.data),
  })

  const { data: slots = [], isFetching: loadingSlots } = useQuery<Slot[]>({
    queryKey: ['reschedule-slots', id, rescheduleDate],
    enabled: showReschedule && !!rescheduleDate,
    queryFn: () =>
      api.get(`/${slug}/availability?date=${rescheduleDate}&itemId=${appt!.item.id}`).then((r) => r.data.slots),
  })

  if (isLoading) return <div className="min-h-screen bg-cream flex items-center justify-center"><div className="text-muted">{t('EN', 'loading')}</div></div>
  if (!appt) return <div className="min-h-screen bg-cream flex items-center justify-center"><div className="text-muted">{t('EN', 'appointmentNotFound')}</div></div>

  const lang = appt.business.language
  const isRTL = lang === 'AR' || lang === 'HE'
  const isCancelled = appt.status === 'CANCELLED' || cancelled

  async function cancelAppointment() {
    const ok = confirm(t(lang, 'cancelConfirm'))
    if (!ok) return
    setLoading(true)
    await api.delete(`/${slug}/appointments/${id}?token=${token}`)
    setCancelled(true)
    setLoading(false)
  }

  function toggleReschedule() {
    setShowReschedule((v) => !v)
    setRescheduleDate('')
    setRescheduleError('')
  }

  async function confirmReschedule(startTime: string) {
    setRescheduling(true)
    setRescheduleError('')
    try {
      await api.patch(`/${slug}/appointments/${id}?token=${token}`, { date: rescheduleDate, startTime })
      setShowReschedule(false)
      setRescheduleDate('')
      await refetch()
      setJustRescheduled(true)
      setTimeout(() => setJustRescheduled(false), 4000)
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setRescheduleError(msg ?? t(lang, 'noTimes'))
    } finally {
      setRescheduling(false)
    }
  }

  const statusLabel = isCancelled
    ? t(lang, 'statusCancelled')
    : appt.status === 'COMPLETED'
      ? t(lang, 'statusCompleted')
      : t(lang, 'statusConfirmed')

  const rows = [
    { label: t(lang, 'service'), value: itemName(appt.item, lang) },
    { label: t(lang, 'date'), value: appt.date.slice(0, 10) },
    { label: t(lang, 'time'), value: `${appt.startTime} – ${appt.endTime}` },
    { label: t(lang, 'name'), value: appt.customer.name },
  ]

  return (
    <div className="min-h-screen bg-cream flex items-center justify-center p-4" dir={isRTL ? 'rtl' : 'ltr'}>
      <div className="w-full max-w-sm">
        <BackButton lang={lang} />
        <h1 className="text-2xl font-bold text-ink text-center mb-2 mt-3">{appt.business.name}</h1>
        <p className="text-muted text-center text-sm mb-8">{t(lang, 'yourAppointment')}</p>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4 mb-6">
          {rows.map(({ label, value }) => (
            <div key={label} className="flex justify-between text-sm">
              <span className="text-muted">{label}</span>
              <span className="text-ink">{value}</span>
            </div>
          ))}
          {appt.notes && (
            <div className="text-sm pt-2 border-t border-line">
              <span className="text-muted block mb-1">{t(lang, 'notes')}</span>
              <span className="text-ink">{appt.notes}</span>
            </div>
          )}
          <div className="flex justify-between text-sm pt-2 border-t border-line">
            <span className="text-muted">{t(lang, 'status')}</span>
            <span className={isCancelled ? 'text-red-600 dark:text-red-400' : appt.status === 'COMPLETED' ? 'text-green-600 dark:text-green-400' : 'text-blue-600 dark:text-blue-400'}>
              {statusLabel}
            </span>
          </div>
        </div>

        {!isCancelled && appt.status === 'CONFIRMED' && (
          <div className="flex gap-3">
            {appt.item.isBookable && (
              <button onClick={toggleReschedule} disabled={loading}
                className="flex-1 border border-line text-ink hover:bg-cream font-medium py-3 rounded-xl transition-colors disabled:opacity-50">
                {t(lang, 'editAppointment')}
              </button>
            )}
            <button onClick={cancelAppointment} disabled={loading}
              className="flex-1 bg-red-50 hover:bg-red-100 border border-red-200 text-red-600 dark:bg-red-950/40 dark:hover:bg-red-900/50 dark:border-red-800/50 dark:text-red-400 font-medium py-3 rounded-xl transition-colors disabled:opacity-50">
              {loading ? '...' : t(lang, 'cancelAppointment')}
            </button>
          </div>
        )}

        {showReschedule && (
          <div className="mt-4 bg-surface border border-line rounded-2xl p-4 space-y-3">
            <p className="text-muted text-sm">{t(lang, 'selectNewTime')}</p>
            <input
              type="date" value={rescheduleDate}
              min={new Date().toISOString().slice(0, 10)}
              onChange={(e) => { setRescheduleDate(e.target.value); setRescheduleError('') }}
              className="w-full bg-cream border border-line rounded-xl px-3 py-2 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral"
            />
            {rescheduleDate && (
              loadingSlots ? (
                <div className="text-center text-muted text-sm py-3">{t(lang, 'loadingTimes')}</div>
              ) : slots.length === 0 ? (
                <div className="text-center text-muted text-sm py-3">{t(lang, 'noTimes')}</div>
              ) : (
                <div className="grid grid-cols-4 gap-2">
                  {slots.map((s) => (
                    <button
                      key={s.start}
                      disabled={rescheduling}
                      onClick={() => confirmReschedule(s.start)}
                      className="bg-cream hover:bg-coral hover:text-white border border-line text-ink text-sm py-2 rounded-lg transition-colors disabled:opacity-50">
                      {s.start}
                    </button>
                  ))}
                </div>
              )
            )}
            {rescheduleError && <p className="text-red-600 dark:text-red-400 text-xs">{rescheduleError}</p>}
          </div>
        )}

        {isCancelled && (
          <div className="text-center text-green-600 dark:text-green-400 text-sm">{t(lang, 'appointmentCancelledMsg')}</div>
        )}
        {justRescheduled && (
          <div className="text-center text-green-600 dark:text-green-400 text-sm mt-3">{t(lang, 'appointmentRescheduledMsg')}</div>
        )}
      </div>
    </div>
  )
}
