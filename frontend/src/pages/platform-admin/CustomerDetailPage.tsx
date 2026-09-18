import { useState } from 'react'
import { useParams, Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { ActivityLogTable, type ActivityLogEntry } from '../../components/platform-admin/ActivityLogTable'
import ThemeToggle from '../../components/ThemeToggle'

type CustomerDetail = { id: string; name: string; familyName: string; phone: string; createdAt: string }

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

  if (!customer) return <div className="min-h-screen bg-cream text-ink p-6">Loading...</div>

  return (
    <div className="min-h-screen bg-cream text-ink p-6">
      <div className="max-w-3xl mx-auto">
        <div className="flex items-center justify-between mb-6">
          <Link to="/platform-admin" className="text-muted hover:text-ink text-sm inline-block">← Back</Link>
          <ThemeToggle />
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6 mb-6">
          <h1 className="text-xl font-bold">{customer.name} {customer.familyName}</h1>
          <p className="text-muted text-sm mb-4">{customer.phone}</p>

          {error && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3 mb-4">{error}</div>}

          <button onClick={handleImpersonate} disabled={impersonating}
            className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold text-sm px-4 py-2.5 rounded-lg transition-colors">
            {impersonating ? 'Logging in...' : 'Log in as this account'}
          </button>
        </div>

        <div className="bg-surface border border-line rounded-2xl p-6">
          <h2 className="font-semibold mb-4">Recent activity</h2>
          <ActivityLogTable entries={activity} />
        </div>
      </div>
    </div>
  )
}
