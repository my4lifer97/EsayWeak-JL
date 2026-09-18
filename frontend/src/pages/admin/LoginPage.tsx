import { useState, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import ThemeToggle from '../../components/ThemeToggle'

type View = 'login' | 'verify'

export default function LoginPage() {
  const { login, verifyEmail, resendVerification } = useAuth()
  const navigate = useNavigate()
  const [view, setView] = useState<View>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [devCode, setDevCode] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const u = await login(email, password)
      navigate(u.mustChangePassword ? '/admin/set-password' : '/admin/dashboard')
    } catch (err: unknown) {
      const resp = (err as { response?: { status?: number; data?: { emailNotVerified?: boolean } } })?.response
      if (resp?.status === 403 && resp.data?.emailNotVerified) {
        try {
          const data = await resendVerification(email)
          if (data.devCode) { setDevCode(data.devCode); setCode(data.devCode) }
        } catch {
          setError('A code was already sent — check your email')
        }
        setView('verify')
      } else {
        setError('Invalid email/username or password')
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
      setError('Invalid or expired code')
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
      setError(status === 429 ? 'Please wait before requesting another code' : 'Could not resend code')
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

  return (
    <div className="relative min-h-screen bg-cream flex items-center justify-center p-4">
      <div className="absolute top-4 end-4"><ThemeToggle /></div>
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-ink mb-2 text-center">EsayWeek</h1>
        <p className="text-muted text-center mb-8">
          {view === 'login' ? 'Sign in to your dashboard' : `Enter the code sent to ${email}`}
        </p>

        {view === 'login' && (
          <form onSubmit={handleSubmit} className="space-y-4">
            {error && (
              <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">
                {error}
              </div>
            )}
            <input
              type="text" required value={email} onChange={(e) => setEmail(e.target.value)}
              placeholder="Email or username" autoComplete="username"
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
            />
            <input
              type="password" required value={password} onChange={(e) => setPassword(e.target.value)}
              placeholder="Password"
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
            />
            <button
              type="submit" disabled={loading}
              className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
            >
              {loading ? '...' : 'Log In'}
            </button>
            <Link to="/admin/forgot-password" className="block text-center text-muted hover:text-ink text-sm">
              Forgot password?
            </Link>
          </form>
        )}

        {view === 'verify' && (
          <div>
            {devCode && (
              <div className="bg-yellow-50 border border-yellow-200 dark:bg-yellow-900/30 dark:border-yellow-700/50 rounded-lg px-3 py-2 text-xs text-yellow-800 dark:text-yellow-300 mb-4 text-center">
                Dev mode — your code is <span className="font-mono font-bold">{devCode}</span>
              </div>
            )}
            <form onSubmit={handleVerify} className="space-y-4">
              <input
                type="text" inputMode="numeric" maxLength={6} required autoFocus
                value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
                placeholder="123456"
                className="w-full bg-surface border border-line rounded-xl px-4 py-4 text-ink text-center text-2xl tracking-widest font-mono placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral"
              />
              {error && (
                <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">
                  {error}
                </div>
              )}
              <button type="submit" disabled={loading || code.length < 6}
                className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
                {loading ? '...' : 'Verify & Continue'}
              </button>
              <button type="button" onClick={handleResend} disabled={loading}
                className="w-full text-muted hover:text-ink text-sm py-1 transition-colors">
                Resend code
              </button>
              <button type="button" onClick={backToLogin}
                className="w-full text-muted hover:text-ink text-sm py-1 transition-colors">
                ← Back to sign in
              </button>
            </form>
          </div>
        )}

        <p className="text-muted text-center mt-6 text-sm">
          No account?{' '}
          <Link to="/request-business-account" className="text-coral-dark hover:underline">Request one</Link>
          {' '}— we review every business before it gets access.
        </p>
      </div>
    </div>
  )
}
