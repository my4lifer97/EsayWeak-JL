import { useState } from 'react'
import { useNavigate, useSearchParams, Link } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'

type View = 'phone' | 'otp'

export default function CustomerLoginPage() {
  const { language: lang } = useAuth()
  const { requestOtp, verifyOtp } = useCustomerAuth()
  const navigate = useNavigate()
  const [params] = useSearchParams()
  const next = params.get('next') ?? '/browse'

  const [view, setView] = useState<View>('phone')
  const [phone, setPhone] = useState('')
  const [otp, setOtp] = useState('')
  const [name, setName] = useState('')
  const [familyName, setFamilyName] = useState('')
  const [isNewCustomer, setIsNewCustomer] = useState(false)
  const [devOtp, setDevOtp] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  async function handleRequestOtp(e: React.FormEvent) {
    e.preventDefault(); setLoading(true); setError('')
    try {
      const data = await requestOtp(phone)
      setIsNewCustomer(data.isNewCustomer)
      if (data.devOtp) { setDevOtp(data.devOtp); setOtp(data.devOtp) }
      setView('otp')
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status
      if (status === 429) setError(t(lang, 'otpRateLimitError'))
      else setError('Could not send code. Check your phone number.')
    } finally { setLoading(false) }
  }

  async function handleVerifyOtp(e: React.FormEvent) {
    e.preventDefault(); setLoading(true); setError('')
    try {
      await verifyOtp(phone, otp, isNewCustomer ? name : undefined, isNewCustomer ? familyName : undefined)
      navigate(next)
    } catch {
      setError(t(lang, 'invalidOtp'))
    } finally { setLoading(false) }
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white flex items-center justify-center px-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <Link to="/" className="text-3xl">✂️</Link>
          <h1 className="text-2xl font-bold text-white mt-3">{t(lang, 'loginTitle')}</h1>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          {view === 'phone' && (
            <form onSubmit={handleRequestOtp} className="space-y-4">
              <p className="text-gray-400 text-sm text-center mb-1">{t(lang, 'enterPhone')}</p>
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
          )}

          {view === 'otp' && (
            <div>
              {devOtp ? (
                <div className="bg-yellow-900/30 border border-yellow-700/50 rounded-lg px-3 py-2 text-xs text-yellow-300 mb-4 text-center">
                  {t(lang, 'devHint')} <span className="font-mono font-bold">{devOtp}</span>
                </div>
              ) : (
                <p className="text-gray-400 text-sm text-center mb-4">{t(lang, 'otpSent')}</p>
              )}

              <form onSubmit={handleVerifyOtp} className="space-y-4">
                {isNewCustomer && (
                  <div className="space-y-3">
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'fullName')}</label>
                      <input type="text" required autoFocus value={name} onChange={(e) => setName(e.target.value)}
                        className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
                    </div>
                    <div>
                      <label className="block text-sm font-medium text-gray-300 mb-1.5">{t(lang, 'familyName')}</label>
                      <input type="text" required value={familyName} onChange={(e) => setFamilyName(e.target.value)}
                        className="w-full bg-gray-800 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
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
                  ← {t(lang, 'changePhone')}
                </button>
              </form>
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
