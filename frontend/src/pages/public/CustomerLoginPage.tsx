import { useState } from 'react'
import { useNavigate, useSearchParams, Link } from 'react-router-dom'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'
import BackButton from '../../components/BackButton'
import LanguageSwitcher from '../../components/customer/LanguageSwitcher'

type View = 'phone' | 'otp'

export default function CustomerLoginPage() {
  const { language: lang, requestOtp, verifyOtp } = useCustomerAuth()
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
    <div className="min-h-screen bg-cream text-ink flex items-center justify-center px-4">
      <div className="w-full max-w-sm">
        <div className="flex items-center justify-between mb-4">
          <BackButton lang={lang} />
          <LanguageSwitcher />
        </div>
        <div className="text-center mb-8">
          <Link to="/" className="text-3xl">✂️</Link>
          <h1 className="text-2xl font-bold text-ink mt-3">{t(lang, 'loginTitle')}</h1>
        </div>

        <div className="bg-white border border-line rounded-2xl p-6">
          {view === 'phone' && (
            <form onSubmit={handleRequestOtp} className="space-y-4">
              <p className="text-muted text-sm text-center mb-1">{t(lang, 'enterPhone')}</p>
              <input
                type="tel" required autoFocus
                value={phone} onChange={(e) => setPhone(e.target.value)}
                placeholder="+1234567890"
                className="w-full bg-cream border border-line rounded-xl px-4 py-3 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral"
              />
              {error && <p className="text-red-600 text-sm text-center">{error}</p>}
              <button type="submit" disabled={loading || !phone}
                className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold py-3 rounded-xl transition-colors">
                {loading ? '...' : t(lang, 'sendCode')}
              </button>
            </form>
          )}

          {view === 'otp' && (
            <div>
              {devOtp ? (
                <div className="bg-yellow-50 border border-yellow-200 rounded-lg px-3 py-2 text-xs text-yellow-800 mb-4 text-center">
                  {t(lang, 'devHint')} <span className="font-mono font-bold">{devOtp}</span>
                </div>
              ) : (
                <p className="text-muted text-sm text-center mb-4">{t(lang, 'otpSent')}</p>
              )}

              <form onSubmit={handleVerifyOtp} className="space-y-4">
                {isNewCustomer && (
                  <div className="space-y-3">
                    <div>
                      <label htmlFor="login-name" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'fullName')}</label>
                      <input id="login-name" type="text" required autoFocus value={name} onChange={(e) => setName(e.target.value)}
                        className="w-full bg-cream border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
                    </div>
                    <div>
                      <label htmlFor="login-family-name" className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'familyName')}</label>
                      <input id="login-family-name" type="text" required value={familyName} onChange={(e) => setFamilyName(e.target.value)}
                        className="w-full bg-cream border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
                    </div>
                  </div>
                )}

                <input
                  type="text" inputMode="numeric" maxLength={6} required autoFocus={!isNewCustomer}
                  value={otp} onChange={(e) => setOtp(e.target.value.replace(/\D/g, ''))}
                  placeholder="123456"
                  className="w-full bg-cream border border-line rounded-xl px-4 py-4 text-ink text-center text-2xl tracking-widest font-mono placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral"
                />

                {error && <p className="text-red-600 text-sm text-center">{error}</p>}

                <button type="submit"
                  disabled={loading || otp.length < 6 || (isNewCustomer && (!name.trim() || !familyName.trim()))}
                  className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold py-3 rounded-xl transition-colors">
                  {loading ? '...' : t(lang, 'verifyCode')}
                </button>

                <button type="button"
                  onClick={() => { setView('phone'); setOtp(''); setName(''); setFamilyName(''); setDevOtp(null); setError('') }}
                  className="w-full text-muted hover:text-ink text-sm py-1 transition-colors">
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
