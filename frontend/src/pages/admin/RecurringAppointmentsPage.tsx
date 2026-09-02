import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { addDays, format } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t, serviceName, type TKey } from '../../lib/i18n'
import CustomerPicker, { type CustomerSelection } from '../../components/admin/CustomerPicker'

type CustomerSummary = { id: string; name: string; familyName: string; phone: string }
type ServiceSummary = { id: string; nameEn: string; nameAr: string; nameHe: string }
type RecurringSkip = { date: string; reason: string }
type RecurringSeries = {
  id: string; customer: CustomerSummary; service: ServiceSummary
  dayOfWeek: number; startTime: string; notes: string | null; isActive: boolean
  startDate: string; endDate: string | null; nextOccurrenceDate: string | null
  recentSkips: RecurringSkip[]
}
type Service = { id: string; nameEn: string; nameAr: string; nameHe: string }
type Slot = { start: string; end: string }

const DAY_KEYS: TKey[] = ['daySun', 'dayMon', 'dayTue', 'dayWed', 'dayThu', 'dayFri', 'daySat']

// The nearest upcoming date that falls on the given weekday -- used both as the series'
// StartDate and as the concrete date to query real availability for (breaks/blocked days/
// existing bookings all depend on an actual calendar date, not just a day-of-week).
function nextDateForWeekday(dayOfWeek: number): string {
  const today = new Date()
  const daysUntil = (dayOfWeek - today.getDay() + 7) % 7
  return format(addDays(today, daysUntil), 'yyyy-MM-dd')
}

export default function RecurringAppointmentsPage() {
  const { language: lang } = useAuth()
  const queryClient = useQueryClient()
  const [showForm, setShowForm] = useState(false)
  const [serviceId, setServiceId] = useState('')
  const [customer, setCustomer] = useState<CustomerSelection | null>(null)
  const [dayOfWeek, setDayOfWeek] = useState<number | null>(null)
  const [slot, setSlot] = useState<Slot | null>(null)
  const [notes, setNotes] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const effectiveDate = dayOfWeek === null ? '' : nextDateForWeekday(dayOfWeek)

  const { data: services = [] } = useQuery<Service[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/services').then((r) => r.data),
  })

  const { data: series = [] } = useQuery<RecurringSeries[]>({
    queryKey: ['recurring-series'],
    queryFn: () => api.get('/admin/recurring').then((r) => r.data),
  })

  const { data: slots = [], isFetching: slotsLoading } = useQuery<Slot[]>({
    queryKey: ['admin-availability', effectiveDate, serviceId],
    queryFn: () => api.get(`/admin/appointments/availability?date=${effectiveDate}&serviceId=${serviceId}`).then((r) => r.data.slots),
    enabled: !!effectiveDate && !!serviceId,
  })

  function resetForm() {
    setServiceId(''); setCustomer(null); setDayOfWeek(null); setSlot(null)
    setNotes(''); setError('')
  }

  async function submit() {
    if (!serviceId || !customer || dayOfWeek === null || !slot) return
    setSubmitting(true); setError('')
    try {
      await api.post('/admin/recurring', {
        ...('customerId' in customer ? { customerId: customer.customerId }
          : 'customerName' in customer ? { customerName: customer.customerName, customerFamilyName: customer.customerFamilyName, customerPhone: customer.customerPhone }
          : {}),
        serviceId, dayOfWeek, startTime: slot.start, notes: notes || undefined,
        startDate: effectiveDate,
      })
      queryClient.invalidateQueries({ queryKey: ['recurring-series'] })
      queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      queryClient.invalidateQueries({ queryKey: ['appointments'] })
      setShowForm(false)
      resetForm()
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? 'Failed to create recurring series')
    } finally { setSubmitting(false) }
  }

  const [actionError, setActionError] = useState('')
  const [actionLoadingId, setActionLoadingId] = useState<string | null>(null)

  async function handleDelete(id: string) {
    if (!confirm(t(lang, 'deleteSeriesConfirm'))) return
    setActionLoadingId(id); setActionError('')
    try {
      await api.delete(`/admin/recurring/${id}`)
      queryClient.invalidateQueries({ queryKey: ['recurring-series'] })
      // Deleting cancels the series' upcoming appointments server-side -- refresh the
      // calendar/table views too so those cancellations show up immediately.
      queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      queryClient.invalidateQueries({ queryKey: ['appointments'] })
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setActionError(msg ?? 'Failed to delete the series. Please try again.')
    } finally { setActionLoadingId(null) }
  }

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-white">{t(lang, 'recurringAppointments')}</h1>
        <button onClick={() => setShowForm(true)}
          className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
          {t(lang, 'newRecurringSeries')}
        </button>
      </div>

      {actionError && (
        <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{actionError}</div>
      )}

      {series.length === 0 ? (
        <div className="text-center text-gray-500 py-16">{t(lang, 'noSeriesYet')}</div>
      ) : (
        <div className="grid gap-3">
          {series.map((s) => (
            <div key={s.id} className="bg-gray-900 border border-gray-800 rounded-xl px-5 py-4">
              <div className="flex items-center justify-between">
                <div>
                  <div className="text-white font-medium">{s.customer.name} {s.customer.familyName} · {s.customer.phone}</div>
                  <div className="text-gray-400 text-sm mt-0.5">{serviceName(s.service, lang)}</div>
                  <div className="text-gray-500 text-xs mt-1">
                    {t(lang, 'everyWeekAt')} {t(lang, DAY_KEYS[s.dayOfWeek])} {t(lang, 'atTime')} {s.startTime}
                    {s.nextOccurrenceDate && ` · ${t(lang, 'nextOccurrence')}: ${s.nextOccurrenceDate}`}
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${s.isActive ? 'bg-blue-900/50 text-blue-300' : 'bg-gray-700/50 text-gray-400'}`}>
                    {t(lang, s.isActive ? 'activeSeries' : 'pausedSeries')}
                  </span>
                  <button onClick={() => handleDelete(s.id)} disabled={actionLoadingId === s.id}
                    className="text-sm text-red-400 hover:text-red-300 disabled:opacity-50">{t(lang, 'delete')}</button>
                </div>
              </div>
              {s.recentSkips.length > 0 && (
                <div className="mt-2 text-amber-400 text-xs">
                  {t(lang, 'missedOccurrence')}: {s.recentSkips[0].date} — {t(lang, 'slotUnavailableReason')}
                  {s.recentSkips.length > 1 && ` (+${s.recentSkips.length - 1})`}
                </div>
              )}
            </div>
          ))}
        </div>
      )}

      {showForm && (
        <div onClick={() => { setShowForm(false); resetForm() }} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
          <div onClick={(e) => e.stopPropagation()} className="bg-gray-900 rounded-2xl p-6 w-full max-w-md border border-gray-800 max-h-[90vh] overflow-y-auto">
            <div className="flex justify-between items-center mb-5">
              <h2 className="text-white font-semibold text-lg">{t(lang, 'newRecurringSeries')}</h2>
              <button onClick={() => { setShowForm(false); resetForm() }} aria-label="Close"
                className="text-gray-500 hover:text-white w-11 h-11 -m-2 flex items-center justify-center rounded-lg hover:bg-gray-800 text-2xl leading-none transition-colors">✕</button>
            </div>
            {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}
            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'service')}</label>
                <select value={serviceId} onChange={(e) => setServiceId(e.target.value)}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500">
                  <option value="">—</option>
                  {services.map((s) => <option key={s.id} value={s.id}>{serviceName(s, lang)}</option>)}
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'customerName')}</label>
                <CustomerPicker lang={lang} value={customer} onChange={setCustomer} />
              </div>

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'dayOfWeekLabel')}</label>
                <div className="flex flex-wrap gap-2">
                  {DAY_KEYS.map((key, i) => (
                    <button key={key} type="button" onClick={() => { setDayOfWeek(i); setSlot(null) }}
                      className={`px-3 py-1.5 rounded-lg text-xs font-medium border transition-colors ${
                        dayOfWeek === i
                          ? 'bg-blue-600 border-blue-500 text-white'
                          : 'bg-gray-800 border-gray-700 text-gray-300 hover:bg-blue-600 hover:border-blue-500'
                      }`}>
                      {t(lang, key)}
                    </button>
                  ))}
                </div>
              </div>

              {dayOfWeek !== null && serviceId && (
                <div>
                  <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'time')}</label>
                  {slotsLoading ? (
                    <div className="text-gray-500 text-sm py-2">{t(lang, 'loadingTimes')}</div>
                  ) : slots.length === 0 ? (
                    <div className="text-gray-500 text-sm py-2">{t(lang, 'noTimes')}</div>
                  ) : (
                    <div className="grid grid-cols-4 gap-2">
                      {slots.map((s) => (
                        <button key={s.start} type="button" onClick={() => setSlot(s)}
                          className={`rounded-lg py-2 text-center text-sm font-medium border transition-colors ${
                            slot?.start === s.start
                              ? 'bg-blue-600 border-blue-500 text-white'
                              : 'bg-gray-800 border-gray-700 hover:bg-blue-600 hover:border-blue-500'
                          }`}>
                          {s.start}
                        </button>
                      ))}
                    </div>
                  )}
                </div>
              )}

              {dayOfWeek !== null && slot && (
                <p className="text-gray-500 text-xs">
                  {t(lang, 'everyWeekAt')} {t(lang, DAY_KEYS[dayOfWeek])} {t(lang, 'atTime')} {slot.start}
                </p>
              )}

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'notes')}</label>
                <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={2}
                  className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none" />
              </div>
              <button type="button" onClick={submit} disabled={!serviceId || !customer || dayOfWeek === null || !slot || submitting}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors mt-2">
                {submitting ? t(lang, 'saving') : t(lang, 'createAppointment')}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
