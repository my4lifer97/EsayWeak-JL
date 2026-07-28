import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { ActivityLogTable } from './BarberDetailPage'

type CustomerDetail = { id: string; name: string; familyName: string; phone: string; createdAt: string }
type ActivityLogEntry = {
  id: string; action: string; description: string; method: string; path: string
  statusCode: number; ipAddress: string | null; createdAt: string; impersonated: boolean
}

export default function PlatformAdminCustomerDetailPage() {
  const { id } = useParams<{ id: string }>()
  const [error, setError] = useState('')
  const [impersonating, setImpersonating] = useState(false)

  const { data: customer } = useQuery<CustomerDetail>({
    queryKey: ['platform-admin-customer', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/customers/${id}`).then((r) => r.data),
  })
  const { data: activity } = useQuery<ActivityLogEntry[]>({
    queryKey: ['platform-admin-customer-activity', id],
    queryFn: () => platformAdminApi.get(`/platform-admin/customers/${id}/activity`).then((r) => r.data),
  })

  async function handleImpersonate() {
    if (!customer) return
    setError('')
    setImpersonating(true)
    try {
      const { data } = await platformAdminApi.post(`/platform-admin/customers/${customer.id}/impersonate`)
      const name = `${customer.name} ${customer.familyName}`.trim()
      localStorage.setItem('customerToken', data.token)
      localStorage.setItem('customerUser', JSON.stringify({
        id: customer.id, name: customer.name, familyName: customer.familyName, phone: customer.phone,
      }))
      localStorage.setItem('impersonation', JSON.stringify({
        type: 'customer', name, returnPath: `/platform-admin/customers/${customer.id}`,
      }))
      window.location.href = '/account/bookings'
    } catch {
      setError('Could not start impersonation')
      setImpersonating(false)
    }
  }

  if (!customer) return <div className="min-h-screen bg-gray-950 text-white p-6">Loading...</div>

  return (
    <div className="min-h-screen bg-gray-950 text-white p-6">
      <div className="max-w-3xl mx-auto">
        <Link to="/platform-admin" className="text-gray-500 hover:text-gray-300 text-sm mb-6 inline-block">← Back</Link>

        <div className="bg-gray-900 border border-gray-800 rounded-2xl p-6 mb-6">
          <h1 className="text-xl font-bold">{customer.name} {customer.familyName}</h1>
          <p className="text-gray-400 text-sm mb-4">{customer.phone}</p>

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
