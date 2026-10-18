import { useState, type FormEvent } from 'react'
import { useNavigate, Link, useSearchParams } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { t, type TKey } from '../../lib/i18n'
import OwnerAuthShell from '../../components/admin/OwnerAuthShell'
import WithEmail from '../../components/WithEmail'

type View = 'login' | 'verify'

export default function LoginPage() {
  const { login, verifyEmail, resendVerification, language: lang } = useAuth()
  const navigate = useNavigate()
  const [view, setView] = useState<View>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [devCode, setDevCode] = useState<string | null>(null)
  const [searchParams] = useSearchParams()
  // Kept as a key, not text, so switching language re-renders the message too.
  // ?disabled=1: api.ts signed this owner out because the account was disabled mid-session.
  const [error, setError] = useState<TKey | ''>(() => searchParams.get('disabled') ? 'authAccountDisabled' : '')
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const u = await login(email, password)
      navigate(u.mustChangePassword ? '/admin/set-password' : '/admin/dashboard')
    } catch (err: unknown) {
      const resp = (err as { response?: { status?: number; data?: { emailNotVerified?: boolean; accountDisabled?: boolean; error?: string } } })?.response
      if (resp?.status === 403 && resp.data?.emailNotVerified) {
        try {
          const data = await resendVerification(email)
          if (data.devCode) { setDevCode(data.devCode); setCode(data.devCode) }
        } catch {
          setError('authCodeAlreadySent')
        }
        setView('verify')
      } else if (resp?.status === 403 && resp.data?.accountDisabled) {
        setError('authAccountDisabled')
      } else if (resp?.status === 429) {
        setError('authTooManyAttempts')
      } else {
        setError('authInvalidCredentials')
      }
    } finally {
      setLoading(false)
    }
  }

  async function handleVerify(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      await verifyEmail(email, code)
      navigate('/admin/dashboard')
    } catch {
      setError('authInvalidCode')
    } finally {
      setLoading(false)
    }
  }

  async function handleResend() {
    setError('')
    setLoading(true)
    try {
      const data = await resendVerification(email)
      if (data.devCode) { setDevCode(data.devCode); setCode(data.devCode) }
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status
      setError(status === 429 ? 'authWaitForCode' : 'authResendFailed')
    } finally {
      setLoading(false)
    }
  }

  function backToLogin() {
    setView('login')
    setCode('')
    setDevCode(null)
    setError('')
  }

  const errorBox = error && (
    <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">
      {t(lang, error)}
    </div>
  )

  return (
    <OwnerAuthShell>
      <h1 className="text-2xl font-bold text-ink mb-2 text-center">EsayWeek</h1>
      <p className="text-muted text-center mb-8">
        {view === 'login' ? t(lang, 'authSignInSubtitle') : <WithEmail text={t(lang, 'authCodeSentTo')} email={email} />}
      </p>

      {view === 'login' && (
        <form onSubmit={handleSubmit} className="space-y-4">
          {errorBox}
          <input
            type="text" required value={email} onChange={(e) => setEmail(e.target.value)}
            placeholder={t(lang, 'authEmailOrUsername')} autoComplete="username"
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <input
            type="password" required value={password} onChange={(e) => setPassword(e.target.value)}
            placeholder={t(lang, 'authPassword')}
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <button
            type="submit" disabled={loading}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
          >
            {loading ? '...' : t(lang, 'authLogIn')}
          </button>
          <Link to="/admin/forgot-password" className="block text-center text-muted hover:text-ink text-sm">
            {t(lang, 'authForgotPassword')}
          </Link>
        </form>
      )}

      {view === 'verify' && (
        <div>
          {devCode && (
            <div className="bg-yellow-50 border border-yellow-200 dark:bg-yellow-900/30 dark:border-yellow-700/50 rounded-lg px-3 py-2 text-xs text-yellow-800 dark:text-yellow-300 mb-4 text-center">
              {t(lang, 'authDevCode')} <span className="font-mono font-bold">{devCode}</span>
            </div>
          )}
          <form onSubmit={handleVerify} className="space-y-4">
            <input
              type="text" inputMode="numeric" maxLength={6} required autoFocus dir="ltr"
              value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
              placeholder="123456"
              className="w-full bg-surface border border-line rounded-xl px-4 py-4 text-ink text-center text-2xl tracking-widest font-mono placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral"
            />
            {errorBox}
            <button type="submit" disabled={loading || code.length < 6}
              className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
              {loading ? '...' : t(lang, 'authVerifyContinue')}
            </button>
            <button type="button" onClick={handleResend} disabled={loading}
              className="w-full text-muted hover:text-ink text-sm py-1 transition-colors">
              {t(lang, 'authResendCode')}
            </button>
            <button type="button" onClick={backToLogin}
              className="w-full text-muted hover:text-ink text-sm py-1 transition-colors">
              {t(lang, 'authBackToSignIn')}
            </button>
          </form>
        </div>
      )}

      <p className="text-muted text-center mt-6 text-sm">
        {t(lang, 'authNoAccount')}{' '}
        <Link to="/request-business-account" className="text-coral-dark hover:underline">{t(lang, 'authRequestOne')}</Link>
        {' '}{t(lang, 'authReviewNote')}
      </p>
    </OwnerAuthShell>
  )
}
