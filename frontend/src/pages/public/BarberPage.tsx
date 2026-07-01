import { useState, useEffect } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { ar, he, enUS } from 'date-fns/locale'
import axios from 'axios'
import { api } from '../../lib/api'
import { t, serviceName } from '../../lib/i18n'

type BarberInfo = {
  slug: string; name: string; description: string | null; logo: string | null
  language: string; isRTL: boolean; activeDays: number[]
  services: { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number }[]
}

type PortalSession = { token: string; customerName: string; slug: string }

type Appointment = {
  id: string; date: string; startTime: string; endTime: string
  status: string; cancelToken: string
  service: { nameEn: string; nameAr: string; nameHe: string }
}

type View = 'phone' | 'otp' | 'dashboard'

const STATUS_COLORS: Record<string, string> = {
  CONFIRMED: 'bg-blue-900/50 text-blue-300 border-blue-800/50',
  COMPLETED: 'bg-green-900/50 text-green-300 border-green-800/50',
  CANCELLED: 'bg-gray-800/50 text-gray-500 border-gray-700/50',
}

function getSession(slug: string): PortalSession | null {
  try {
    const s: PortalSession = JSON.parse(localStorage.getItem('portalSession') ?? 'null')
    return s?.slug === slug ? s : null
  } catch { return null }
}

export default function BarberPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const { data: barber, isLoading } = useQuery<BarberInfo>({
    queryKey: ['barber', slug],
    queryFn: () => api.get(`/${slug}/info`).then((r) => r.data),
  })

  const lang = barber?.language ?? 'EN'
  const dir = barber?.isRTL ? 'rtl' : 'ltr'
  const dateLocale = lang === 'AR' ? ar : lang === 'HE' ? he : enUS

  // Auth state
  const [view, setView] = useState<View>(() => getSession(slug ?? '') ? 'dashboard' : 'phone')
  const [session, setSession] = useState<PortalSession | null>(() => getSession(slug ?? ''))

  // Login form state
  const [phone, setPhone] = useState('')
  const [name, setName] = useState('')
  const [familyName, setFamilyName] = useState('')
  const [otp, setOtp] = useState('')
  const [isNewCustomer, setIsNewCustomer] = useState(false)
  const [devOtp, setDevOtp] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  // Appointments
  const [cancelling, setCancelling] = useState<string | null>(null)

  useEffect(() => {
    const s = getSession(slug ?? '')
    if (s) { setSession(s); setView('dashboard') }
    else { setSession(null); setView('phone') }
  }, [slug])

  const { data: appointments = [], isLoading: apptLoading } = useQuery<Appointment[]>({
    queryKey: ['portal-appointments', slug, session?.token],
    enabled: view === 'dashboard' && !!session,
    queryFn: () =>
      axios.get(`/api/${slug}/portal/appointments`, {
        headers: { Authorization: `Bearer ${session!.token}` },
      }).then((r) => r.data),
  })

  async function requestOtp(e: React.FormEvent) {
    e.preventDefault(); setLoading(true); setError('')
    try {
      const { data } = await api.post(`/${slug}/portal/otp`, { phone })
      setIsNewCustomer(data.isNewCustomer)
      if (data.devOtp) { setDevOtp(data.devOtp); setOtp(data.devOtp) }
      setView('otp')
    } catch { setError('Could not send code. Check your phone number.') }
    finally { setLoading(false) }
  }

  async function verifyOtp(e: React.FormEvent) {
    e.preventDefault(); setLoading(true); setError('')
    try {
      const { data } = await api.post(`/${slug}/portal/verify`, {
        phone, otp,
        name: isNewCustomer ? name : undefined,
        familyName: isNewCustomer ? familyName : undefined,
      })
      const s: PortalSession = { token: data.token, customerName: data.customerName, slug: slug! }
      localStorage.setItem('portalSession', JSON.stringify(s))
      setSession(s)
      queryClient.invalidateQueries({ queryKey: ['portal-appointments', slug] })
      setView('dashboard')
    } catch { setError(t(lang, 'invalidOtp')) }
    finally { setLoading(false) }
  }

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
    setSession(null); setPhone(''); setOtp(''); setName(''); setFamilyName(''); setDevOtp(null); setError('')
    setView('phone')
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <div className="text-gray-500">{t('EN', 'loading')}</div>
      </div>
    )
  }
  if (!barber) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <div className="text-gray-400">{t('EN', 'barberNotFound')}</div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white" dir={dir}>
      <div className="max-w-lg mx-auto px-4 py-10">

        {/* Barber header — always visible */}
        <div className="text-center mb-8">
          <div className="text-5xl mb-4">✂️</div>
          <h1 className="text-3xl font-bold text-white">{barber.name}</h1>
          {barber.description && (
            <p className="text-gray-400 mt-2 text-sm">{barber.description}</p>
          )}
        </div>

        {/* ── DASHBOARD ── */}
        {view === 'dashboard' && session && (
          <div>
            <div className="flex items-center justify-between mb-6">
              <div>
                <p className="text-white font-semibold">{session.customerName}</p>
                <p className="text-gray-500 text-sm">{t(lang, 'myAppointments')}</p>
              </div>
              <button onClick={signOut}
                className="text-gray-500 hover:text-gray-300 text-sm transition-colors">
                {t(lang, 'signOutPortal')}
              </button>
            </div>

            <button onClick={() => navigate(`/${slug}/book`)}
              className="flex items-center justify-center gap-2 w-full bg-blue-600 hover:bg-blue-700 text-white font-bold py-4 rounded-2xl transition-colors mb-6">
              ✂️ {t(lang, 'bookNew')}
            </button>

            {apptLoading ? (
              <div className="text-center text-gray-500 py-12">{t(lang, 'loading')}</div>
            ) : appointments.length === 0 ? (
              <div className="text-center text-gray-500 py-12">
                <div className="text-4xl mb-3">📅</div>
                <p>{t(lang, 'noAppointmentsYet')}</p>
              </div>
            ) : (
              <div className="space-y-3">
                {appointments.map((appt) => (
                  <div key={appt.id}
                    className={`bg-gray-900 border rounded-2xl p-4 transition-opacity ${appt.status !== 'CONFIRMED' ? 'border-gray-800 opacity-60' : 'border-gray-700'}`}>
                    <div className="flex items-start justify-between gap-3">
                      <div className="flex-1 min-w-0">
                        <div className="font-semibold text-white truncate">
                          {serviceName(appt.service, lang)}
                        </div>
                        <div className="text-gray-400 text-sm mt-1">
                          {format(parseISO(appt.date), 'EEE, MMM d yyyy', { locale: dateLocale })}
                          {' · '}{appt.startTime}–{appt.endTime}
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
                ))}
              </div>
            )}
          </div>
        )}

        {/* ── PHONE FORM ── */}
        {view === 'phone' && (
          <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
            <p className="text-gray-400 text-sm text-center mb-5">{t(lang, 'enterPhone')}</p>
            <form onSubmit={requestOtp} className="space-y-4">
              <input
                type="tel" required autoFocus
                value={phone} onChange={(e) => setPhone(e.target.value)}
                placeholder="+1234567890"
                className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-3 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              {error && <p className="text-red-400 text-sm text-center">{error}</p>}
              <button type="submit" disabled={loading || !phone}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-3 rounded-xl transition-colors">
                {loading ? '...' : t(lang, 'sendCode')}
              </button>
            </form>
          </div>
        )}

        {/* ── OTP FORM ── */}
        {view === 'otp' && (
          <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
            {devOtp ? (
              <div className="bg-yellow-900/30 border border-yellow-700/50 rounded-lg px-3 py-2 text-xs text-yellow-300 mb-4 text-center">
                {t(lang, 'devHint')} <span className="font-mono font-bold">{devOtp}</span>
              </div>
            ) : (
              <p className="text-gray-400 text-sm text-center mb-4">{t(lang, 'otpSent')}</p>
            )}

            <form onSubmit={verifyOtp} className="space-y-4">
              {isNewCustomer && (
                <div className="space-y-3">
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-1.5">
                      {t(lang, 'fullName')}
                    </label>
                    <input
                      type="text" required autoFocus
                      value={name} onChange={(e) => setName(e.target.value)}
                      className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-300 mb-1.5">
                      {t(lang, 'familyName')}
                    </label>
                    <input
                      type="text" required
                      value={familyName} onChange={(e) => setFamilyName(e.target.value)}
                      className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
                    />
                  </div>
                </div>
              )}

              <input
                type="text" inputMode="numeric" maxLength={6} required autoFocus={!isNewCustomer}
                value={otp} onChange={(e) => setOtp(e.target.value.replace(/\D/g, ''))}
                placeholder="123456"
                className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-4 text-white text-center text-2xl tracking-widest font-mono placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />

              {error && <p className="text-red-400 text-sm text-center">{error}</p>}

              <button type="submit"
                disabled={loading || otp.length < 6 || (isNewCustomer && (!name.trim() || !familyName.trim()))}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-3 rounded-xl transition-colors">
                {loading ? '...' : t(lang, 'verifyCode')}
              </button>

              <button type="button"
                onClick={() => { setView('phone'); setOtp(''); setName(''); setFamilyName(''); setDevOtp(null); setError('') }}
                className="w-full text-gray-500 hover:text-gray-300 text-sm py-1 transition-colors">
                ← {t(lang, 'back')}
              </button>
            </form>
          </div>
        )}

      </div>
    </div>
  )
}
