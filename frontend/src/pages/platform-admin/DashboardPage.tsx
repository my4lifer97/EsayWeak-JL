import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { usePlatformAdminAuth } from '../../lib/platformAdminAuth'
import ThemeToggle from '../../components/ThemeToggle'

type BusinessSummary = { id: string; name: string; email: string; slug: string; subscriptionStatus: string }
type CustomerSummary = { id: string; name: string; familyName: string; phone: string }

export default function PlatformAdminDashboardPage() {
  const { user, logout } = usePlatformAdminAuth()
  const [businessSearch, setBusinessSearch] = useState('')
  const [customerSearch, setCustomerSearch] = useState('')

  const { data: businesses } = useQuery<BusinessSummary[]>({
    queryKey: ['platform-admin-businesses', businessSearch],
    queryFn: () => platformAdminApi.get('/platform-admin/businesses', { params: { search: businessSearch || undefined } }).then((r) => r.data),
  })
  const { data: customers } = useQuery<CustomerSummary[]>({
    queryKey: ['platform-admin-customers', customerSearch],
    queryFn: () => platformAdminApi.get('/platform-admin/customers', { params: { search: customerSearch || undefined } }).then((r) => r.data),
  })

  return (
    <div className="min-h-screen bg-cream text-ink p-6">
      <div className="max-w-5xl mx-auto">
        <div className="flex items-center justify-between mb-8">
          <h1 className="text-2xl font-bold">Platform Admin</h1>
          <div className="flex items-center gap-4 text-sm text-muted">
            <Link to="/platform-admin/requests" className="hover:text-ink transition-colors">Business requests</Link>
            <span>{user?.name}</span>
            <button onClick={logout} className="hover:text-ink transition-colors">Sign out</button>
            <ThemeToggle />
          </div>
        </div>

        <div className="grid md:grid-cols-2 gap-6">
          <section className="bg-surface border border-line rounded-2xl p-5">
            <h2 className="font-semibold mb-3">Business owners</h2>
            <input
              value={businessSearch} onChange={(e) => setBusinessSearch(e.target.value)}
              placeholder="Search by name, email, or URL"
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-coral"
            />
            <div className="space-y-1 max-h-96 overflow-y-auto">
              {businesses?.map((b) => (
                <Link key={b.id} to={`/platform-admin/businesses/${b.id}`}
                  className="flex items-center justify-between px-3 py-2.5 rounded-lg hover:bg-cream transition-colors">
                  <div>
                    <div className="text-sm font-medium">{b.name}</div>
                    <div className="text-xs text-muted">{b.email} · /{b.slug}</div>
                  </div>
                  <span className={`text-xs px-2 py-0.5 rounded-full ${
                    b.subscriptionStatus === 'ACTIVE' ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400'
                      : b.subscriptionStatus === 'TRIAL' ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400' : 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-400'
                  }`}>{b.subscriptionStatus}</span>
                </Link>
              ))}
              {businesses?.length === 0 && <p className="text-muted text-sm px-3 py-2">No results</p>}
            </div>
          </section>

          <section className="bg-surface border border-line rounded-2xl p-5">
            <h2 className="font-semibold mb-3">Customers</h2>
            <input
              value={customerSearch} onChange={(e) => setCustomerSearch(e.target.value)}
              placeholder="Search by name or phone"
              className="w-full bg-cream border border-line rounded-lg px-3 py-2 text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-coral"
            />
            <div className="space-y-1 max-h-96 overflow-y-auto">
              {customers?.map((c) => (
                <Link key={c.id} to={`/platform-admin/customers/${c.id}`}
                  className="flex items-center justify-between px-3 py-2.5 rounded-lg hover:bg-cream transition-colors">
                  <div>
                    <div className="text-sm font-medium">{c.name} {c.familyName}</div>
                    <div className="text-xs text-muted">{c.phone}</div>
                  </div>
                </Link>
              ))}
              {customers?.length === 0 && <p className="text-muted text-sm px-3 py-2">No results</p>}
            </div>
          </section>
        </div>
      </div>
    </div>
  )
}
