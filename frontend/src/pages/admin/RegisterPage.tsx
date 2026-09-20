import { useState, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import ThemeToggle from '../../components/ThemeToggle'

type View = 'form' | 'verify'
type BusinessType = { id: string; key: string; displayNameEn: string }

export default function RegisterPage() {
  const { verifyEmail, resendVerification } = useAuth()
  const navigate = useNavigate()
  const [view, setView] = useState<View>('form')
  const [form, setForm] = useState({ name: '', email: '', password: '', confirmPassword: '', slug: '', businessTypeId: '' })

  const { data: businessTypes } = useQuery<BusinessType[]>({
    queryKey: ['business-types'],
    queryFn: () => api.get('/business-types').then((r) => r.data),
  })
  const [code, setCode] = useState('')
  const [devCode, setDevCode] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  function set(field: string) {
    return (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm((f) => ({ ...f, [field]: e.target.value }))
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    if (form.password !== form.confirmPassword) {
      setError('Passwords do not match')
      return
    }
    if (!form.businessTypeId) {
      setError('Please choose a business type')
      return
    }
    setLoading(true)
    try {
      const { name, email, password, slug, businessTypeId } = form
      const { data } = await api.post('/auth/register', { name, email, password, slug, businessTypeId })
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
    <div className="relative min-h-screen bg-cream flex items-center justify-center p-4">
      <div className="absolute top-4 end-4"><ThemeToggle /></div>
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-ink mb-2 text-center">
          {view === 'form' ? 'Create Account' : 'Verify your email'}
        </h1>
        <p className="text-muted text-center mb-8">
          {view === 'form' ? 'Start your 30-day free trial' : `Enter the code sent to ${form.email}`}
        </p>

        {view === 'form' && (
          <form onSubmit={handleSubmit} className="space-y-4">
            {error && (
              <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">
                {error}
              </div>
            )}
            <input type="text" required value={form.name} onChange={set('name')} placeholder="Business Name"
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
            <input type="email" required value={form.email} onChange={set('email')} placeholder="Email"
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
            <input type="password" required value={form.password} onChange={set('password')} placeholder="Password"
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
            <input type="password" required value={form.confirmPassword} onChange={set('confirmPassword')} placeholder="Confirm Password"
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
            <div>
              <input type="text" required value={form.slug} onChange={set('slug')} placeholder="booking-url-slug"
                className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
              <p className="text-muted text-xs mt-1.5 px-1">
                yoursite.com/{form.slug || 'your-slug'} — lowercase letters, numbers, hyphens only
              </p>
            </div>
            <select required value={form.businessTypeId} onChange={set('businessTypeId')}
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral">
              <option value="">Business type…</option>
              {businessTypes?.map((t) => (
                <option key={t.id} value={t.id}>{t.displayNameEn}</option>
              ))}
            </select>
            <button type="submit" disabled={loading}
              className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
              {loading ? '...' : 'Create Account'}
            </button>
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
            </form>
          </div>
        )}

        <p className="text-muted text-center mt-6 text-sm">
          Already have an account?{' '}
          <Link to="/admin/login" className="text-coral-dark hover:underline">Sign in</Link>
        </p>
      </div>
    </div>
  )
}
