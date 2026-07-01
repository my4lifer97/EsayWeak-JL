import { useEffect, useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import axios from 'axios'
import { api } from '../../lib/api'
import { t, serviceName } from '../../lib/i18n'

type PortalSession = { token: string; customerName: string; slug: string }

type Appointment = {
  id: string; date: string; startTime: string; endTime: string
  status: string; cancelToken: string
  service: { nameEn: string; nameAr: string; nameHe: string }
}

type BarberInfo = { name: string; language: string; isRTL: boolean }

const STATUS_COLORS: Record<string, string> = {
  CONFIRMED: 'bg-blue-900/50 text-blue-300 border-blue-800/50',
  COMPLETED: 'bg-green-900/50 text-green-300 border-green-800/50',
  CANCELLED: 'bg-gray-800/50 text-gray-500 border-gray-700/50',
}

export default function CustomerAppointmentsPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const [session, setSession] = useState<PortalSession | null>(null)
  const [cancelling, setCancelling] = useState<string | null>(null)

  useEffect(() => {
    try {
      const raw = localStorage.getItem('portalSession')
      const s: PortalSession = raw ? JSON.parse(raw) : null
      if (!s || s.slug !== slug) { navigate(`/${slug}/portal`, { replace: true }); return }
      setSession(s)
    } catch { navigate(`/${slug}/portal`, { replace: true }) }
  }, [slug, navigate])

  const { data: barber } = useQuery<BarberInfo>({
    queryKey: ['barber', slug],
    queryFn: () => api.get(`/${slug}/info`).then((r) => r.data),
  })

  const { data: appointments = [], isLoading } = useQuery<Appointment[]>({
    queryKey: ['portal-appointments', slug],
    enabled: !!session,
    queryFn: () =>
      axios.get(`/api/${slug}/portal/appointments`, {
        headers: { Authorization: `Bearer ${session!.token}` },
      }).then((r) => r.data),
  })

  const lang = barber?.language ?? 'EN'
  const dir = barber?.isRTL ? 'rtl' : 'ltr'

  async function cancelAppointment(appt: Appointment) {
    if (!confirm(t(lang, 'cancelConfirm'))) return
    setCancelling(appt.id)
    try {
      await api.delete(`/${slug}/appointments/${appt.id}?token=${appt.cancelToken}`)
      queryClient.invalidateQueries({ queryKey: ['portal-appointments', slug] })
    } finally { setCancelling(null) }
  }

  function signOut() {
    localStorage.removeItem('portalSession')
    navigate(`/${slug}/portal`)
  }

  if (!session) return null

  return (
    <div className="min-h-screen bg-gray-950 text-white" dir={dir}>
      <div className="max-w-lg mx-auto px-4 py-10">

        {/* Header */}
        <div className="flex items-center justify-between mb-8">
          <div>
            <h1 className="text-xl font-bold">{session.customerName}</h1>
            <p className="text-gray-400 text-sm">{t(lang, 'myAppointments')}</p>
          </div>
          <button onClick={signOut}
            className="text-gray-500 hover:text-gray-300 text-sm transition-colors">
            {t(lang, 'signOutPortal')}
          </button>
        </div>

        {/* Book new */}
        <Link to={`/${slug}/book`}
          className="flex items-center justify-center gap-2 w-full bg-blue-600 hover:bg-blue-700 text-white font-semibold py-3 rounded-2xl transition-colors mb-6 text-sm">
          ✂️ {t(lang, 'bookNew')}
        </Link>

        {/* Appointments */}
        {isLoading ? (
          <div className="text-center text-gray-500 py-16">{t(lang, 'loading')}</div>
        ) : appointments.length === 0 ? (
          <div className="text-center text-gray-500 py-16">
            <div className="text-4xl mb-3">📅</div>
            <p>{t(lang, 'noAppointmentsYet')}</p>
          </div>
        ) : (
          <div className="space-y-3">
            {appointments.map((appt) => {
              const isPast = appt.status !== 'CONFIRMED'
              return (
                <div key={appt.id}
                  className={`bg-gray-900 border rounded-2xl p-4 ${isPast ? 'border-gray-800 opacity-70' : 'border-gray-700'}`}>
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex-1 min-w-0">
                      <div className="font-semibold text-white truncate">
                        {serviceName(appt.service, lang)}
                      </div>
                      <div className="text-gray-400 text-sm mt-1">
                        {format(parseISO(appt.date), 'EEE, MMM d yyyy')} · {appt.startTime}–{appt.endTime}
                      </div>
                    </div>
                    <span className={`shrink-0 text-xs font-medium px-2.5 py-1 rounded-full border ${STATUS_COLORS[appt.status] ?? STATUS_COLORS.CANCELLED}`}>
                      {t(lang, appt.status === 'CONFIRMED' ? 'statusConfirmed' : appt.status === 'COMPLETED' ? 'statusCompleted' : 'statusCancelled')}
                    </span>
                  </div>

                  {appt.status === 'CONFIRMED' && (
                    <button
                      disabled={cancelling === appt.id}
                      onClick={() => cancelAppointment(appt)}
                      className="mt-3 w-full border border-red-800/60 text-red-400 hover:bg-red-900/30 text-sm font-medium py-2 rounded-xl transition-colors disabled:opacity-50">
                      {cancelling === appt.id ? '...' : t(lang, 'cancelAppointment')}
                    </button>
                  )}
                </div>
              )
            })}
          </div>
        )}
      </div>
    </div>
  )
}
