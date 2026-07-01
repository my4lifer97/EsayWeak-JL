import { useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t, serviceName } from '../../lib/i18n'

type AppointmentDetail = {
  id: string; date: string; startTime: string; endTime: string; status: string; notes: string | null; cancelToken: string
  service: { nameEn: string; nameAr: string; nameHe: string }
  customer: { name: string; phone: string }
  barber: { name: string; language: string }
}

export default function AppointmentPage() {
  const { slug, id } = useParams<{ slug: string; id: string }>()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''
  const [loading, setLoading] = useState(false)
  const [cancelled, setCancelled] = useState(false)

  const { data: appt, isLoading } = useQuery<AppointmentDetail>({
    queryKey: ['appointment', id],
    queryFn: () => api.get(`/${slug}/appointments/${id}`).then((r) => r.data),
  })

  if (isLoading) return <div className="min-h-screen bg-gray-950 flex items-center justify-center"><div className="text-gray-500">{t('EN', 'loading')}</div></div>
  if (!appt) return <div className="min-h-screen bg-gray-950 flex items-center justify-center"><div className="text-gray-400">{t('EN', 'appointmentNotFound')}</div></div>

  const lang = appt.barber.language
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

  const statusLabel = isCancelled
    ? t(lang, 'statusCancelled')
    : appt.status === 'COMPLETED'
      ? t(lang, 'statusCompleted')
      : t(lang, 'statusConfirmed')

  const rows = [
    { label: t(lang, 'service'), value: serviceName(appt.service, lang) },
    { label: t(lang, 'date'), value: appt.date.slice(0, 10) },
    { label: t(lang, 'time'), value: `${appt.startTime} – ${appt.endTime}` },
    { label: t(lang, 'name'), value: appt.customer.name },
  ]

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4" dir={isRTL ? 'rtl' : 'ltr'}>
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-white text-center mb-2">{appt.barber.name}</h1>
        <p className="text-gray-400 text-center text-sm mb-8">{t(lang, 'yourAppointment')}</p>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 space-y-4 mb-6">
          {rows.map(({ label, value }) => (
            <div key={label} className="flex justify-between text-sm">
              <span className="text-gray-400">{label}</span>
              <span className="text-white">{value}</span>
            </div>
          ))}
          {appt.notes && (
            <div className="text-sm pt-2 border-t border-gray-800">
              <span className="text-gray-400 block mb-1">{t(lang, 'notes')}</span>
              <span className="text-white">{appt.notes}</span>
            </div>
          )}
          <div className="flex justify-between text-sm pt-2 border-t border-gray-800">
            <span className="text-gray-400">{t(lang, 'status')}</span>
            <span className={isCancelled ? 'text-red-400' : appt.status === 'COMPLETED' ? 'text-green-400' : 'text-blue-400'}>
              {statusLabel}
            </span>
          </div>
        </div>

        {!isCancelled && appt.status === 'CONFIRMED' && (
          <button onClick={cancelAppointment} disabled={loading}
            className="w-full bg-red-900/40 hover:bg-red-900/60 border border-red-800 text-red-300 font-medium py-3 rounded-xl transition-colors disabled:opacity-50">
            {loading ? '...' : t(lang, 'cancelAppointment')}
          </button>
        )}
        {isCancelled && (
          <div className="text-center text-green-400 text-sm">{t(lang, 'appointmentCancelledMsg')}</div>
        )}
      </div>
    </div>
  )
}
