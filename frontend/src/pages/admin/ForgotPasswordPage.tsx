import { useState, FormEvent } from 'react'
import { useNavigate, Link } from 'react-router-dom'
import { useAuth } from '../../lib/auth'

type View = 'email' | 'reset'

export default function ForgotPasswordPage() {
  const { forgotPassword, resetPassword } = useAuth()
  const navigate = useNavigate()
  const [view, setView] = useState<View>('email')
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [confirmNewPassword, setConfirmNewPassword] = useState('')
  const [devCode, setDevCode] = useState<string | null>(null)
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  async function handleRequestCode(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      const data = await forgotPassword(email)
      if (data.devCode) { setDevCode(data.devCode); setCode(data.devCode) }
      setView('reset')
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status
      if (status === 404) setError('No account found with that email')
      else if (status === 429) setError('Please wait before requesting another code')
      else setError('Could not send reset code')
    } finally {
      setLoading(false)
    }
  }

  async function handleReset(e: FormEvent) {
    e.preventDefault()
    setError('')
    if (newPassword !== confirmNewPassword) {
      setError('Passwords do not match')
      return
    }
    setLoading(true)
    try {
      await resetPassword(email, code, newPassword)
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
      const data = await forgotPassword(email)
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
        <h1 className="text-2xl font-bold text-white mb-2 text-center">Reset your password</h1>
        <p className="text-gray-400 text-center mb-8">
          {view === 'email' ? 'Enter your account email' : `Enter the code sent to ${email}`}
        </p>

        {view === 'email' && (
          <form onSubmit={handleRequestCode} className="space-y-4">
            {error && (
              <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">
                {error}
              </div>
            )}
            <input
              type="email" required autoFocus value={email} onChange={(e) => setEmail(e.target.value)}
              placeholder="Email"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <button type="submit" disabled={loading}
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
              {loading ? '...' : 'Send reset code'}
            </button>
          </form>
        )}

        {view === 'reset' && (
          <div>
            {devCode && (
              <div className="bg-yellow-900/30 border border-yellow-700/50 rounded-lg px-3 py-2 text-xs text-yellow-300 mb-4 text-center">
                Dev mode — your code is <span className="font-mono font-bold">{devCode}</span>
              </div>
            )}
            <form onSubmit={handleReset} className="space-y-4">
              <input
                type="text" inputMode="numeric" maxLength={6} required autoFocus
                value={code} onChange={(e) => setCode(e.target.value.replace(/\D/g, ''))}
                placeholder="123456"
                className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-4 text-white text-center text-2xl tracking-widest font-mono placeholder-gray-600 focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              <input
                type="password" required value={newPassword} onChange={(e) => setNewPassword(e.target.value)}
                placeholder="New password"
                className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              <input
                type="password" required value={confirmNewPassword} onChange={(e) => setConfirmNewPassword(e.target.value)}
                placeholder="Confirm new password"
                className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
              />
              {error && (
                <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">
                  {error}
                </div>
              )}
              <button type="submit" disabled={loading || code.length < 6}
                className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors">
                {loading ? '...' : 'Reset password'}
              </button>
              <button type="button" onClick={handleResend} disabled={loading}
                className="w-full text-gray-500 hover:text-gray-300 text-sm py-1 transition-colors">
                Resend code
              </button>
            </form>
          </div>
        )}

        <p className="text-gray-500 text-center mt-6 text-sm">
          <Link to="/admin/login" className="text-blue-400 hover:underline">← Back to sign in</Link>
        </p>
      </div>
    </div>
  )
}
