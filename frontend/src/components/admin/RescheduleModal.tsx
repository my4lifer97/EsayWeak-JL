import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'

type Slot = { start: string; end: string }

export default function RescheduleModal({
  lang, appointmentId, itemId, onClose, onDone,
}: {
  lang: string
  appointmentId: string
  itemId: string
  onClose: () => void
  onDone: () => void
}) {
  const queryClient = useQueryClient()
  const [date, setDate] = useState('')
  const [slot, setSlot] = useState<Slot | null>(null)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [notifyCustomer, setNotifyCustomer] = useState(true)

  // Same query key as the Settings page; customers are messaged only when a WhatsApp number is linked.
  const { data: settings } = useQuery<{ whatsAppNumber: string | null }>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  const { data: slots = [], isFetching: slotsLoading } = useQuery<Slot[]>({
    queryKey: ['admin-availability', date, itemId, appointmentId],
    // Excluding the appointment being moved, so it can shift by less than its own length.
    queryFn: () => api.get(`/admin/appointments/availability?date=${date}&itemId=${itemId}&excludeAppointmentId=${appointmentId}`).then((r) => r.data.slots),
    enabled: !!date,
  })

  async function submit() {
    if (!slot) return
    setSubmitting(true); setError('')
    try {
      await api.patch(`/admin/appointments/${appointmentId}/reschedule`, { date, startTime: slot.start, notifyCustomer })
      queryClient.invalidateQueries({ queryKey: ['dashboard'] })
      queryClient.invalidateQueries({ queryKey: ['appointments'] })
      onDone()
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? t(lang, 'failedReschedule'))
    } finally { setSubmitting(false) }
  }

  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 w-full max-w-md border border-line max-h-[90vh] overflow-y-auto">
        <div className="flex justify-between items-center mb-5">
          <h2 className="text-ink font-semibold text-lg">{t(lang, 'rescheduleAppointment')}</h2>
          <button onClick={onClose} aria-label={t(lang, 'close')}
            className="text-muted hover:text-ink w-11 h-11 -m-2 flex items-center justify-center rounded-lg hover:bg-cream text-2xl leading-none transition-colors">✕</button>
        </div>
        {error && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}

        <div className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'date')}</label>
            <input type="date" value={date} onChange={(e) => { setDate(e.target.value); setSlot(null) }}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          </div>

          {date && (
            <div>
              <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'time')}</label>
              {slotsLoading ? (
                <div className="text-muted text-sm py-2">{t(lang, 'loadingTimes')}</div>
              ) : slots.length === 0 ? (
                <div className="text-muted text-sm py-2">{t(lang, 'noTimes')}</div>
              ) : (
                <div className="grid grid-cols-4 gap-2">
                  {slots.map((s) => (
                    <button key={s.start} type="button" onClick={() => setSlot(s)}
                      className={`rounded-lg py-2 text-center text-sm font-medium border transition-colors ${
                        slot?.start === s.start
                          ? 'bg-coral border-coral-dark text-white'
                          : 'bg-cream border-line hover:bg-coral hover:border-coral-dark hover:text-white'
                      }`}>
                      {s.start}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}

          {settings?.whatsAppNumber && (
            <label className="flex items-center gap-2 text-sm text-ink cursor-pointer">
              <input type="checkbox" checked={notifyCustomer} onChange={(e) => setNotifyCustomer(e.target.checked)}
                className="w-4 h-4 accent-coral" />
              {t(lang, 'notifyCustomerWhatsApp')}
            </label>
          )}

          <button type="button" onClick={submit} disabled={!slot || submitting}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors mt-2">
            {submitting ? t(lang, 'saving') : t(lang, 'rescheduleAppointment')}
          </button>
        </div>
      </div>
    </div>
  )
}
