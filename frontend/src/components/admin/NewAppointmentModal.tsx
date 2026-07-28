import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t, serviceName } from '../../lib/i18n'
import CustomerPicker, { type CustomerSelection } from './CustomerPicker'

type Service = { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number }
type Slot = { start: string; end: string }

export default function NewAppointmentModal({
  lang, defaultDate, onClose,
}: {
  lang: string
  defaultDate?: string
  onClose: () => void
}) {
  const queryClient = useQueryClient()
  const [serviceId, setServiceId] = useState('')
  const [customer, setCustomer] = useState<CustomerSelection | null>(null)
  const [date, setDate] = useState(defaultDate ?? '')
  const [slot, setSlot] = useState<Slot | null>(null)
  const [showCustomTime, setShowCustomTime] = useState(false)
  const [customTime, setCustomTime] = useState('')
  const [forceBook, setForceBook] = useState(false)
  const [notes, setNotes] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const { data: services = [] } = useQuery<Service[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/services').then((r) => r.data),
  })

  const { data: slots = [], isFetching: slotsLoading } = useQuery<Slot[]>({
    queryKey: ['admin-availability', date, serviceId],
    queryFn: () => api.get(`/admin/appointments/availability?date=${date}&serviceId=${serviceId}`).then((r) => r.data.slots),
    enabled: !!date && !!serviceId && !showCustomTime,
  })

  const startTime = showCustomTime ? customTime : slot?.start ?? ''
  const canSubmit = !!serviceId && !!customer && !!date && !!startTime && (!showCustomTime || forceBook)

  async function submit() {
    if (!canSubmit || !customer) return
    setSubmitting(true); setError('')
    try {
      await api.post('/admin/appointments', {
        ...('customerId' in customer ? { customerId: customer.customerId } : { customerName: customer.customerName, customerPhone: customer.customerPhone }),
        serviceId, date, startTime, notes: notes || undefined, force: showCustomTime ? forceBook : false,
      })
      queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      queryClient.invalidateQueries({ queryKey: ['appointments'] })
      onClose()
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? 'Failed to create appointment')
    } finally { setSubmitting(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div className="bg-gray-900 rounded-2xl p-6 w-full max-w-md border border-gray-800 max-h-[90vh] overflow-y-auto">
        <div className="flex justify-between items-center mb-5">
          <h2 className="text-white font-semibold text-lg">{t(lang, 'newAppointment')}</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white text-xl">✕</button>
        </div>
        {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'service')}</label>
            <select value={serviceId} onChange={(e) => { setServiceId(e.target.value); setSlot(null) }}
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
            <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'date')}</label>
            <input type="date" value={date} onChange={(e) => { setDate(e.target.value); setSlot(null) }}
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
          </div>

          {date && serviceId && !showCustomTime && (
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

          <button type="button" onClick={() => { setShowCustomTime((v) => !v); setForceBook(false); setSlot(null) }}
            className="text-sm text-blue-400 hover:text-blue-300">
            {showCustomTime ? `← ${t(lang, 'back')}` : t(lang, 'enterCustomTime')}
          </button>

          {showCustomTime && (
            <div className="space-y-2">
              <input type="time" value={customTime} onChange={(e) => setCustomTime(e.target.value)}
                className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
              <label className="flex items-start gap-2 text-sm text-gray-300">
                <input type="checkbox" checked={forceBook} onChange={(e) => setForceBook(e.target.checked)} className="mt-0.5" />
                <span>{t(lang, 'forceBookLabel')}</span>
              </label>
              <p className="text-gray-500 text-xs">{t(lang, 'forceBookHint')}</p>
            </div>
          )}

          <div>
            <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'notes')}</label>
            <textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={2}
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-white placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-500 resize-none" />
          </div>

          <button type="button" onClick={submit} disabled={!canSubmit || submitting}
            className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors mt-2">
            {submitting ? t(lang, 'saving') : t(lang, 'createAppointment')}
          </button>
        </div>
      </div>
    </div>
  )
}
