import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { ActivityLogTable, type ActivityLogEntry } from '../../components/platform-admin/ActivityLogTable'

type BusinessDetail = {
  id: string; name: string; email: string; slug: string; phone: string | null
  trialEndsAt: string; subscriptionStatus: string; createdAt: string; twilioNumber: string | null
}

export default function PlatformAdminBusinessDetailPage() {
  const { id } = useParams<{ id: string }>()
  const queryClient = useQueryClient()
  const [error, setError] = useState('')
  const [impersonating, setImpersonating] = useState(false)
  const [twilioNumber, setTwilioNumber] = useState('')
  const [twilioInitialized, setTwilioInitialized] = useState(false)
  const [savingTwilio, setSavingTwilio] = useState(false)
  const [twilioError, setTwilioError] = useState('')

  const { data: business } = useQuery<BusinessDetail>({
    queryKey: ['platform-admin-business', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/businesses/${id}`).then((r) => r.data),
  })

  if (business && !twilioInitialized) {
    setTwilioNumber(business.twilioNumber ?? '')
    setTwilioInitialized(true)
  }

  async function handleSaveTwilioNumber() {
    if (!business) return
    setSavingTwilio(true); setTwilioError('')
    try {
      await platformAdminApi.patch(`/platform-admin/businesses/${business.id}/twilio-number`, {
        twilioNumber: twilioNumber || null,
      })
      queryClient.invalidateQueries({ queryKey: ['platform-admin-business', id] })
    } catch {
      setTwilioError('Could not save')
    } finally {
      setSavingTwilio(false)
    }
  }
  const { data: activity } = useQuery<ActivityLogEntry[]>({
    queryKey: ['platform-admin-business-activity', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/businesses/${id}/activity`).then((r) => r.data),
  })

  async function handleImpersonate() {
    if (!business) return
    setError('')
    setImpersonating(true)
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/businesses/${business.id}/impersonate`)
      localStorage.setItem('token', data.token)
      localStorage.setItem('user', JSON.stringify({ id: business.id, name: business.name, email: business.email, slug: business.slug }))
      localStorage.setItem('impersonation', JSON.stringify({
        type: 'business', name: business.name, returnPath: `/platform-admin/businesses/${business.id}`,
      }))
      window.location.href = '/admin/dashboard'
    } catch {
      setError('Could not start impersonation')
      setImpersonating(false)
    }
  }

  if (!business) return <div className="min-h-screen bg-gray-950 text-white p-6">Loading...</div>

  return (
    <div className="min-h-screen bg-gray-950 text-white p-6">
      <div className="max-w-3xl mx-auto">
        <Link to="/platform-admin" className="text-gray-500 hover:text-gray-300 text-sm mb-6 inline-block">← Back</Link>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 mb-6">
          <div className="flex items-start justify-between mb-4">
            <div>
              <h1 className="text-xl font-bold">{business.name}</h1>
              <p className="text-gray-400 text-sm">{business.email}</p>
              <p className="text-gray-500 text-sm">/{business.slug} {business.phone && `· ${business.phone}`}</p>
            </div>
            <span className={`text-xs px-2 py-1 rounded-full ${
              business.subscriptionStatus === 'ACTIVE' ? 'bg-green-900/40 text-green-300'
                : business.subscriptionStatus === 'TRIAL' ? 'bg-blue-900/40 text-blue-300' : 'bg-red-900/40 text-red-300'
            }`}>{business.subscriptionStatus}</span>
          </div>

          {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}

          <button onClick={handleImpersonate} disabled={impersonating}
            className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
            {impersonating ? 'Logging in...' : 'Log in as this account'}
          </button>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 mb-6">
          <h2 className="font-semibold mb-1">WhatsApp Number</h2>
          <p className="text-gray-500 text-sm mb-3">
            Which of the platform's Twilio WhatsApp senders this business's chatbot uses.
          </p>
          {twilioError && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-3">{twilioError}</div>}
          <div className="flex gap-2">
            <input type="text" value={twilioNumber} onChange={(e) => setTwilioNumber(e.target.value)}
              placeholder="+14155238886"
              className="flex-1 bg-gray-800 border border-gray-700 rounded-lg px-3 py-2.5 text-white font-mono focus:outline-none focus:ring-2 focus:ring-blue-500" />
            <button onClick={handleSaveTwilioNumber} disabled={savingTwilio}
              className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
              {savingTwilio ? 'Saving...' : 'Save'}
            </button>
          </div>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          <h2 className="font-semibold mb-4">Recent activity</h2>
          <ActivityLogTable entries={activity} />
        </div>
      </div>
    </div>
  )
}
