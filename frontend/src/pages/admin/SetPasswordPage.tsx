import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import ThemeToggle from '../../components/ThemeToggle'

export default function SetPasswordPage() {
  const { changePassword, logout } = useAuth()
  const navigate = useNavigate()
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmPassword, setConfirmPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    if (newPassword.length < 6) {
      setError('Password must be at least 6 characters')
      return
    }
    if (newPassword !== confirmPassword) {
      setError('Passwords do not match')
      return
    }
    if (newPassword === currentPassword) {
      setError('Choose a password different from the temporary one')
      return
    }
    setLoading(true)
    try {
      await changePassword(currentPassword, newPassword)
      navigate('/admin/dashboard')
    } catch (err: unknown) {
      const res = (err as { response?: { status?: number; data?: { error?: string; currentPasswordIncorrect?: boolean; samePassword?: boolean } } })?.response
      if (res?.data?.currentPasswordIncorrect) setError('The temporary password is incorrect')
      else if (res?.data?.samePassword) setError('Choose a password different from the temporary one')
      else if (res?.status === 429 && res.data?.error) setError(res.data.error)
      else setError('Could not set your password. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="relative min-h-screen bg-cream flex items-center justify-center p-4">
      <div className="absolute top-4 end-4"><ThemeToggle /></div>
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-ink mb-2 text-center">Set your password</h1>
        <p className="text-muted text-center mb-8">
          You're signing in with a temporary password. Choose a new one to continue.
        </p>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">
              {error}
            </div>
          )}
          <input
            type="password" required autoFocus autoComplete="current-password"
            value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)}
            placeholder="Temporary password"
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <input
            type="password" required autoComplete="new-password" value={newPassword} onChange={(e) => setNewPassword(e.target.value)}
            placeholder="New password"
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <input
            type="password" required value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)}
            placeholder="Confirm new password"
            className="w-full bg-surface border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <button
            type="submit" disabled={loading}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
          >
            {loading ? '...' : 'Set password & continue'}
          </button>
        </form>
        <button type="button" onClick={() => { logout(); navigate('/admin/login') }}
          className="w-full mt-4 text-muted hover:text-ink text-sm">
          Sign out
        </button>
      </div>
    </div>
  )
}
