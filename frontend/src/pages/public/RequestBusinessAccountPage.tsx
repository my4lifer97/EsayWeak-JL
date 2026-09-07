import { useState, type FormEvent } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../../lib/api'

type BusinessType = { id: string; key: string; displayNameEn: string }

export default function RequestBusinessAccountPage() {
  const [businessName, setBusinessName] = useState('')
  const [ownerName, setOwnerName] = useState('')
  const [email, setEmail] = useState('')
  const [phone, setPhone] = useState('')
  const [businessTypeId, setBusinessTypeId] = useState('')
  const [error, setError] = useState('')
  const [loading, setLoading] = useState(false)
  const [submitted, setSubmitted] = useState(false)

  const { data: businessTypes } = useQuery<BusinessType[]>({
    queryKey: ['business-types'],
    queryFn: () => api.get('/business-types').then((r) => r.data),
  })

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setError('')
    setLoading(true)
    try {
      await api.post('/business-owner-requests', {
        businessName, ownerName, email, phone,
        businessTypeId: businessTypeId || null,
      })
      setSubmitted(true)
    } catch (err: unknown) {
      const resp = (err as { response?: { status?: number; data?: { error?: string } } })?.response
      if (resp?.status === 409) setError('A request from this email is already pending review.')
      else setError(resp?.data?.error ?? 'Could not submit your request. Please try again.')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="min-h-screen bg-gray-950 flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <h1 className="text-2xl font-bold text-white mb-2 text-center">Request a business account</h1>
        <p className="text-gray-400 text-center mb-8">
          Tell us about your business and we'll be in touch to set up your account.
        </p>

        {submitted ? (
          <div className="bg-green-900/30 border border-green-700/50 rounded-xl px-4 py-6 text-center">
            <p className="text-green-300 font-medium mb-2">Thanks — your request has been submitted.</p>
            <p className="text-gray-400 text-sm">We'll review it and reach out with your account details.</p>
          </div>
        ) : (
          <form onSubmit={handleSubmit} className="space-y-4">
            {error && (
              <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3">
                {error}
              </div>
            )}
            <input
              required value={businessName} onChange={(e) => setBusinessName(e.target.value)}
              placeholder="Business name"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <input
              required value={ownerName} onChange={(e) => setOwnerName(e.target.value)}
              placeholder="Your name"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <input
              type="email" required value={email} onChange={(e) => setEmail(e.target.value)}
              placeholder="Email"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <input
              required value={phone} onChange={(e) => setPhone(e.target.value)}
              placeholder="Phone"
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <select
              value={businessTypeId} onChange={(e) => setBusinessTypeId(e.target.value)}
              className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white focus:outline-none focus:ring-2 focus:ring-blue-500"
            >
              <option value="">Business type (optional)</option>
              {businessTypes?.map((t) => (
                <option key={t.id} value={t.id}>{t.displayNameEn}</option>
              ))}
            </select>
            <button
              type="submit" disabled={loading}
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-bold py-3 rounded-xl transition-colors"
            >
              {loading ? '...' : 'Submit request'}
            </button>
          </form>
        )}

        <p className="text-gray-500 text-center mt-6 text-sm">
          <Link to="/admin/login" className="text-blue-400 hover:underline">← Back to sign in</Link>
        </p>
      </div>
    </div>
  )
}
