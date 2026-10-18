import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { t, type TKey } from '../../lib/i18n'
import OwnerAuthShell from '../../components/admin/OwnerAuthShell'

export default function SetPasswordPage() {
  const { changePassword, logout, language: lang } = useAuth()
  const navigate = useNavigate()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState<{ key: TKey } | null>(null)
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError(null)
    if (newPassword.length < 6) {
      setError({ key: 'authPasswordTooShort' })
      return
    }
    if (newPassword !== confirmPassword) {
      setError({ key: 'authPasswordsMismatch' })
      return
    }
    if (newPassword === currentPassword) {
      setError({ key: 'authSamePassword' })
      return
    }
    setLoading(true)
    try {
      await changePassword(currentPassword, newPassword)
      navigate('/admin/dashboard')
    } catch (err: unknown) {
      const res = (err as { response?: { status?: number; data?: { error?: string; currentPasswordIncorrect?: boolean; samePassword?: boolean } } })?.response
      if (res?.data?.currentPasswordIncorrect) setError({ key: 'authTempPasswordWrong' })
      else if (res?.data?.samePassword) setError({ key: 'authSamePassword' })
      else if (res?.status === 429) setError({ key: 'authTooManyAttempts' })
      else setError({ key: 'authSetPasswordFailed' })
    } finally {
      setLoading(false)
    }
  }

  return (
    <OwnerAuthShell>
      <h1 className="text-2xl font-bold text-ink mb-2 text-center">{t(lang, 'authSetPasswordTitle')}</h1>
      <p className="text-muted text-center mb-8">{t(lang, 'authSetPasswordIntro')}</p>

      <form onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">
            {t(lang, error.key)}
          </div>
        )}
        <input
          type="password" required autoFocus autoComplete="current-password"
          value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)}
          placeholder={t(lang, 'authTempPassword')}
          className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
        />
        <input
          type="password" required autoComplete="new-password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)}
          placeholder={t(lang, 'authNewPassword')}
          className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
        />
        <input
          type="password" required value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)}
          placeholder={t(lang, 'authConfirmNewPassword')}
          className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
        />
        <button
          type="submit" disabled={loading}
          className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
        >
          {loading ? '...' : t(lang, 'authSetPasswordContinue')}
        </button>
      </form>
      <button type="button" onClick={() => { logout(); navigate('/admin/login') }}
        className="w-full mt-4 text-muted hover:text-ink text-sm">
        {t(lang, 'signOut')}
      </button>
    </OwnerAuthShell>
  )
}
