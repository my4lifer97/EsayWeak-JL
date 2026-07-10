import { useState, FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useAuth } from '../../lib/auth'

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
      await login(email, password)
      navigate('/admin/dashboard')
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
        setError('Invalid email or password')
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
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-white mb-2 text-center">EsayWeek</h1>
        <p className="text-gray-400 text-center mb-8">
          {view === 'login' ? 'Sign in to your dashboard' : `Enter the code sent to ${email}`}
        </p>

        {view === 'login' && (
          <form onSubmit={handleSubmit} className="space-y-4">
            {error && (
              <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">
                {error}
              </div>
            )}
            <input
              type="email" required value={email} onChange={(e) => setEmail(e.target.value)}
              placeholder="Email"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <input
              type="password" required value={password} onChange={(e) => setPassword(e.target.value)}
              placeholder="Password"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <button
              type="submit" disabled={loading}
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
            >
              {loading ? '...' : 'Log In'}
            </button>
          </form>
        )}

        {view === 'verify' && (
          <div>
            {devCode && (
              <div className="bg-yellow-900/30 border border-yellow-700/50 rounded-lg px-3 py-2 text-xs text-yellow-300 mb-4 text-center">
                Dev mode — your code is <span className="font-mono font-bold">{devCode}</span>
              </div>
            )}
            <form onSubmit={handleVerify} className="space-y-4">
              <input
                type="text" inputMode="numeric" maxLength={6} required autoFocus
                value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
                placeholder="123456"
                className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-4 text-white text-center text-2xl tracking-widest font-mono placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              {error && (
                <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">
                  {error}
                </div>
              )}
              <button type="submit" disabled={loading || code.length < 6}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
                {loading ? '...' : 'Verify & Continue'}
              </button>
              <button type="button" onClick={handleResend} disabled={loading}
                className="w-full text-gray-500 hover:text-gray-300 text-sm py-1 transition-colors">
                Resend code
              </button>
              <button type="button" onClick={backToLogin}
                className="w-full text-gray-500 hover:text-gray-300 text-sm py-1 transition-colors">
                ← Back to sign in
              </button>
            </form>
          </div>
        )}

        <p className="text-gray-500 text-center mt-6 text-sm">
          No account?{' '}
          <Link to="/admin/register" className="text-blue-400 hover:underline">Create one</Link>
        </p>
      </div>
    </div>
  )
}
