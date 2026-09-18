import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../../lib/auth'

export default function SetPasswordPage() {
  const { changePassword } = useAuth()
  const navigate = useNavigate()
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
    setLoading(true)
    try {
      await changePassword(newPassword)
      navigate('/admin/dashboard')
    } catch {
      setError('Could not set your password. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-cream flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-ink mb-2 text-center">Set your password</h1>
        <p className="text-muted text-center mb-8">
          You're signing in with a temporary password. Choose a new one to continue.
        </p>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="bg-red-50 border border-red-200 text-red-700 text-sm rounded-lg px-4 py-3">
              {error}
            </div>
          )}
          <input
            type="password" required autoFocus value={newPassword} onChange={(e) => setNewPassword(e.target.value)}
            placeholder="New password"
            className="w-full bg-white border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <input
            type="password" required value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)}
            placeholder="Confirm new password"
            className="w-full bg-white border border-line rounded-xl px-4 py-3 text-ink focus:outline-none focus:ring-2 focus:ring-coral"
          />
          <button
            type="submit" disabled={loading}
            className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
          >
            {loading ? '...' : 'Set password & continue'}
          </button>
        </form>
      </div>
    </div>
  )
}
