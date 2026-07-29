import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { ActivityLogTable, type ActivityLogEntry } from '../../components/platform-admin/ActivityLogTable'

type BarberDetail = {
  id: string; name: string; email: string; slug: string; phone: string | null
  trialEndsAt: string; subscriptionStatus: string; createdAt: string
}

export default function PlatformAdminBarberDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [error, setError] = useState('')
  const [impersonating, setImpersonating] = useState(false)

  const { data: barber } = useQuery<BarberDetail>({
    queryKey: ['platform-admin-barber', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/barbers/${id}`).then((r) => r.data),
  })
  const { data: activity } = useQuery<ActivityLogEntry[]>({
    queryKey: ['platform-admin-barber-activity', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/barbers/${id}/activity`).then((r) => r.data),
  })

  async function handleImpersonate() {
    if (!barber) return
    setError('')
    setImpersonating(true)
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/barbers/${barber.id}/impersonate`)
      localStorage.setItem('token', data.token)
      localStorage.setItem('user', JSON.stringify({ id: barber.id, name: barber.name, email: barber.email, slug: barber.slug }))
      localStorage.setItem('impersonation', JSON.stringify({
        type: 'barber', name: barber.name, returnPath: `/platform-admin/barbers/${barber.id}`,
      }))
      window.location.href = '/admin/dashboard'
    } catch {
      setError('Could not start impersonation')
      setImpersonating(false)
    }
  }

  if (!barber) return <div className="min-h-screen bg-gray-950 text-white p-6">Loading...</div>

  return (
    <div className="min-h-screen bg-gray-950 text-white p-6">
      <div className="max-w-3xl mx-auto">
        <Link to="/platform-admin" className="text-gray-500 hover:text-gray-300 text-sm mb-6 inline-block">← Back</Link>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 mb-6">
          <div className="flex items-start justify-between mb-4">
            <div>
              <h1 className="text-xl font-bold">{barber.name}</h1>
              <p className="text-gray-400 text-sm">{barber.email}</p>
              <p className="text-gray-500 text-sm">/{barber.slug} {barber.phone && `· ${barber.phone}`}</p>
            </div>
            <span className={`text-xs px-2 py-1 rounded-full ${
              barber.subscriptionStatus === 'ACTIVE' ? 'bg-green-900/40 text-green-300'
                : barber.subscriptionStatus === 'TRIAL' ? 'bg-blue-900/40 text-blue-300' : 'bg-red-900/40 text-red-300'
            }`}>{barber.subscriptionStatus}</span>
          </div>

          {error && <div className="bg-red-900/40 border border-red-700 text-red-300 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}

          <button onClick={handleImpersonate} disabled={impersonating}
            className="bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
            {impersonating ? 'Logging in...' : 'Log in as this account'}
          </button>
        </div>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6">
          <h2 className="font-semibold mb-4">Recent activity</h2>
          <ActivityLogTable entries={activity} />
        </div>
      </div>
    </div>
  )
}
