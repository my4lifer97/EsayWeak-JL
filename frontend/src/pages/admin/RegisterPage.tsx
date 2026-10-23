import { useState, type FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t, type TKey } from '../../lib/i18n'
import OwnerAuthShell from '../../components/admin/OwnerAuthShell'
import WithEmail from '../../components/WithEmail'

type View = 'form' | 'verify'
type BusinessType = { id: string; key: string; displayNameEn: string; displayNameAr: string; displayNameHe: string }

export default function RegisterPage() {
  const { verifyEmail, resendVerification, language: lang } = useAuth()
  const navigate = useNavigate()
  const [view, setView] = useState<View>('form')
  const [form, setForm] = useState({ name: '', email: '', password: '', confirmPassword: '', slug: '', businessTypeId: '' })

  const { data: businessTypes } = useQuery<BusinessType[]>({
    queryKey: ['business-types'],
    queryFn: () => api.get('/business-types').then((r) => r.data),
  })
  const [code, setCode] = useState('')
  const [devCode, setDevCode] = useState<string | null>(null)
  // A translation key, or the server's own message (which is not translated).
  const [error, setError] = useState<{ key: TKey } | { text: string } | null>(null)
  const [loading, setLoading] = useState(false)

  function set(field: string) {
    return (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setForm((f) => ({ ...f, [field]: e.target.value }))
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (form.password !== form.confirmPassword) {
      setError({ key: 'authPasswordsMismatch' })
      return
    }
    if (!form.businessTypeId) {
      setError({ key: 'authChooseBusinessType' })
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
      setError(msg ? { text: msg } : { key: 'authRegistrationFailed' })
    } finally {
      setLoading(false)
    }
  }

  async function handleVerify(e: FormEvent) {
    e.preventDefault()
    setError(null)
    setLoading(true)
    try {
      await verifyEmail(form.email, code)
      navigate('/admin/dashboard')
    } catch {
      setError({ key: 'authInvalidCode' })
    } finally {
      setLoading(false)
    }
  }

  async function handleResend() {
    setError(null)
    setLoading(true)
    try {
      const data = await resendVerification(form.email)
      if (data.devCode) { setDevCode(data.devCode); setCode(data.devCode) }
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status
      setError({ key: status === 429 ? 'authWaitForCode' : 'authResendFailed' })
    } finally {
      setLoading(false)
    }
  }

  function typeName(bt: BusinessType) {
    if (lang === 'AR') return bt.displayNameAr
    if (lang === 'HE') return bt.displayNameHe
    return bt.displayNameEn
  }

  const errorBox = error && (
    <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">
      {'key' in error ? t(lang, error.key) : error.text}
    </div>
  )

  return (
    <OwnerAuthShell>
      <h1 className="text-2xl font-bold text-ink mb-2 text-center">
        {view === 'form' ? t(lang, 'authCreateAccount') : t(lang, 'authVerifyEmail')}
      </h1>
      <p className="text-muted text-center mb-8">
        {view === 'form' ? t(lang, 'authFreeTrial') : <WithEmail text={t(lang, 'authCodeSentTo')} email={form.email} />}
      </p>

      {view === 'form' && (
        <form onSubmit={handleSubmit} className="space-y-4">
          {errorBox}
          <input type="text" required value={form.name} onChange={set('name')} placeholder={t(lang, 'businessName')}
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          <input type="email" required value={form.email} onChange={set('email')} placeholder={t(lang, 'authEmail')}
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          <input type="password" required value={form.password} onChange={set('password')} placeholder={t(lang, 'authPassword')}
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          <input type="password" required value={form.confirmPassword} onChange={set('confirmPassword')} placeholder={t(lang, 'authConfirmPassword')}
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
          <div>
            <input type="text" required value={form.slug} onChange={set('slug')} maxLength={60} placeholder="booking-url-slug" dir="ltr"
              className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral" />
            <p className="text-muted text-xs mt-1.5 px-1">
              <bdi dir="ltr">yoursite.com/{form.slug || 'your-slug'}</bdi> {t(lang, 'authSlugHint')}
            </p>
          </div>
          <select required value={form.businessTypeId} onChange={set('businessTypeId')}
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral">
            <option value="">{t(lang, 'authBusinessType')}</option>
            {businessTypes?.map((bt) => (
              <option key={bt.id} value={bt.id}>{typeName(bt)}</option>
            ))}
          </select>
          <button type="submit" disabled={loading}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
            {loading ? '...' : t(lang, 'authCreateAccount')}
          </button>
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
          </form>
        </div>
      )}

      <p className="text-muted text-center mt-6 text-sm">
        {t(lang, 'authHaveAccount')}{' '}
        <Link to="/admin/login" className="text-coral-dark hover:underline">{t(lang, 'authSignIn')}</Link>
      </p>
    </OwnerAuthShell>
  )
}
