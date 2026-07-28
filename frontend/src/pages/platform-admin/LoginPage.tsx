import { useEffect, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { usePlatformAdminAuth } from '../../lib/platformAdminAuth'
import { platformAdminApi } from '../../lib/platformAdminApi'

export default function PlatformAdminLoginPage() {
  const { login, bootstrap } = usePlatformAdminAuth()
  const navigate = useNavigate()
  const [bootstrapAvailable, setBootstrapAvailable] = useState<boolean | null>(null)
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    platformAdminApi.get('/platform-admin/bootstrap-available')
      .then(({ data }) => setBootstrapAvailable(data.available))
      .catch(() => setBootstrapAvailable(false))
  }, [])

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      if (bootstrapAvailable) await bootstrap(email, password, name)
      else await login(email, password)
      navigate('/platform-admin')
    } catch {
      setError(bootstrapAvailable ? 'Could not create admin account' : 'Invalid email or password')
    } finally {
      setLoading(false)
    }
  }

  if (bootstrapAvailable === null) return null

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-white mb-2 text-center">Platform Admin</h1>
        <p className="text-gray-400 text-center mb-8">
          {bootstrapAvailable ? 'Create the admin account' : 'Sign in'}
        </p>

        <form onSubmit={handleSubmit} className="space-y-4">
          {error && (
            <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">
              {error}
            </div>
          )}
          {bootstrapAvailable && (
            <input
              type="text" required value={name} onChange={(e) => setName(e.target.value)}
              placeholder="Name"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
          )}
          <input
            type="email" required value={email} onChange={(e) => setEmail(e.target.value)}
            placeholder="Email"
            className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <input
            type="password" required value={password} onChange={(e) => setPassword(e.target.value)}
            placeholder="Password" minLength={bootstrapAvailable ? 8 : undefined}
            className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
          />
          <button
            type="submit" disabled={loading}
            className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
          >
            {loading ? '...' : bootstrapAvailable ? 'Create Admin Account' : 'Sign In'}
          </button>
        </form>
      </div>
    </div>
  )
}
