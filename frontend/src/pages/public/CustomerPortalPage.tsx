import { useState } from 'react'
import { useParams, useNavigate } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'

type BarberInfo = { name: string; language: string; isRTL: boolean }

type Step = 'phone' | 'otp'

export default function CustomerPortalPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()

  const { data: barber } = useQuery<BarberInfo>({
    queryKey: ['barber', slug],
    queryFn: () => api.get(`/${slug}/info`).then((r) => r.data),
  })

  const lang = barber?.language ?? 'EN'
  const dir = barber?.isRTL ? 'rtl' : 'ltr'

  const [step, setStep] = useState<Step>('phone')
  const [phone, setPhone] = useState('')
  const [name, setName] = useState('')
  const [otp, setOtp] = useState('')
  const [isNewCustomer, setIsNewCustomer] = useState(false)
  const [devOtp, setDevOtp] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  async function requestOtp(e: React.FormEvent) {
    e.preventDefault()
    setLoading(true); setError('')
    try {
      const { data } = await api.post(`/${slug}/portal/otp`, { phone })
      setIsNewCustomer(data.isNewCustomer)
      if (data.devOtp) {
        setDevOtp(data.devOtp)
        setOtp(data.devOtp)
      }
      setStep('otp')
    } catch {
      setError('Could not send code. Check your phone number.')
    } finally { setLoading(false) }
  }

  async function verifyOtp(e: React.FormEvent) {
    e.preventDefault()
    setLoading(true); setError('')
    try {
      const { data } = await api.post(`/${slug}/portal/verify`, {
        phone, otp,
        name: isNewCustomer ? name : undefined,
      })
      localStorage.setItem('portalSession', JSON.stringify({ token: data.token, customerName: data.customerName, slug }))
      navigate(`/${slug}/portal/appointments`)
    } catch {
      setError(t(lang, 'invalidOtp'))
    } finally { setLoading(false) }
  }

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4" dir={dir}>
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <div className="text-4xl mb-3">✂️</div>
          <h1 className="text-2xl font-bold text-white">{barber?.name ?? '...'}</h1>
          <p className="text-gray-400 text-sm mt-1">{t(lang, 'myAppointments')}</p>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          {step === 'phone' && (
            <form onSubmit={requestOtp} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1.5">
                  {t(lang, 'enterPhone')}
                </label>
                <input
                  type="tel" required autoFocus
                  value={phone} onChange={(e) => setPhone(e.target.value)}
                  placeholder="+1234567890"
                  className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-3 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
              </div>
              {error && <p className="text-red-400 text-sm">{error}</p>}
              <button type="submit" disabled={loading || !phone}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-3 rounded-xl transition-colors">
                {loading ? '...' : t(lang, 'sendCode')}
              </button>
            </form>
          )}

          {step === 'otp' && (
            <form onSubmit={verifyOtp} className="space-y-4">
              {devOtp && (
                <div className="bg-yellow-900/30 border border-yellow-700/50 rounded-lg px-3 py-2 text-xs text-yellow-300">
                  {t(lang, 'devHint')} <span className="font-mono font-bold">{devOtp}</span>
                </div>
              )}
              {!devOtp && (
                <p className="text-gray-400 text-sm text-center">{t(lang, 'otpSent')}</p>
              )}

              {isNewCustomer && (
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
              )}

              <div>
                <label className="block text-sm font-medium text-gray-300 mb-1.5">
                  {t(lang, 'enterOtp')}
                </label>
                <input
                  type="text" inputMode="numeric" maxLength={6} required
                  value={otp} onChange={(e) => setOtp(e.target.value.replace(/\D/g, ''))}
                  placeholder="123456"
                  className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-3 text-white text-center text-2xl tracking-widest font-mono placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-500"
                />
              </div>

              {error && <p className="text-red-400 text-sm text-center">{error}</p>}

              <button type="submit" disabled={loading || otp.length < 6 || (isNewCustomer && !name)}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-3 rounded-xl transition-colors">
                {loading ? '...' : t(lang, 'verifyCode')}
              </button>

              <button type="button" onClick={() => { setStep('phone'); setOtp(''); setDevOtp(null); setError('') }}
                className="w-full text-gray-500 hover:text-gray-300 text-sm py-1 transition-colors">
                ← {t(lang, 'back')}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  )
}
