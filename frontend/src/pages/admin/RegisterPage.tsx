import { useState, FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'

type View = 'form' | 'verify'

export default function RegisterPage() {
  const { verifyEmail, resendVerification } = useAuth()
  const navigate = useNavigate()
  const [view, setView] = useState<View>('form')
  const [form, setForm] = useState({ name: '', email: '', password: '', confirmPassword: '', slug: '' })
  const [code, setCode] = useState('')
  const [devCode, setDevCode] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  function set(field: string) {
    return (e: React.ChangeEvent<HTMLInputElement>) => setForm((f) => ({ ...f, [field]: e.target.value }))
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    if (form.password !== form.confirmPassword) {
      setError('Passwords do not match')
      return
    }
    setLoading(true)
    try {
      const { name, email, password, slug } = form
      const { data } = await api.post('/auth/register', { name, email, password, slug })
      if (data.devCode) { setDevCode(data.devCode); setCode(data.devCode) }
      setView('verify')
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? 'Registration failed')
    } finally {
      setLoading(false)
    }
  }

  async function handleVerify(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      await verifyEmail(form.email, code)
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
      const data = await resendVerification(form.email)
      if (data.devCode) { setDevCode(data.devCode); setCode(data.devCode) }
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status
      setError(status === 429 ? 'Please wait before requesting another code' : 'Could not resend code')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-white mb-2 text-center">
          {view === 'form' ? 'Create Account' : 'Verify your email'}
        </h1>
        <p className="text-gray-400 text-center mb-8">
          {view === 'form' ? 'Start your 30-day free trial' : `Enter the code sent to ${form.email}`}
        </p>

        {view === 'form' && (
          <form onSubmit={handleSubmit} className="space-y-4">
            {error && (
              <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">
                {error}
              </div>
            )}
            <input type="text" required value={form.name} onChange={set('name')} placeholder="Business Name"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
            <input type="email" required value={form.email} onChange={set('email')} placeholder="Email"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
            <input type="password" required value={form.password} onChange={set('password')} placeholder="Password"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
            <input type="password" required value={form.confirmPassword} onChange={set('confirmPassword')} placeholder="Confirm Password"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
            <div>
              <input type="text" required value={form.slug} onChange={set('slug')} placeholder="booking-url-slug"
                className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500" />
              <p className="text-gray-500 text-xs mt-1.5 px-1">
                yoursite.com/{form.slug || 'your-slug'} — lowercase letters, numbers, hyphens only
              </p>
            </div>
            <button type="submit" disabled={loading}
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
              {loading ? '...' : 'Create Account'}
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
            </form>
          </div>
        )}

        <p className="text-gray-500 text-center mt-6 text-sm">
          Already have an account?{' '}
          <Link to="/admin/login" className="text-blue-400 hover:underline">Sign in</Link>
        </p>
      </div>
    </div>
  )
}
