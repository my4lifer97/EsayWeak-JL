import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { platformAdminApi } from '../../lib/platformAdminApi'
import { usePlatformAdminAuth } from '../../lib/platformAdminAuth'

type BarberSummary = { id: string; name: string; email: string; slug: string; subscriptionStatus: string }
type CustomerSummary = { id: string; name: string; familyName: string; phone: string }

export default function PlatformAdminDashboardPage() {
  const { user, logout } = usePlatformAdminAuth()
  const [barberSearch, setBarberSearch] = useState('')
  const [customerSearch, setCustomerSearch] = useState('')

  const { data: barbers } = useQuery<BarberSummary[]>({
    queryKey: ['platform-admin-barbers', barberSearch],
    queryFn: () => platformAdminApi.get('/platform-admin/barbers', { params: { search: barberSearch || undefined } }).then((r) => r.data),
  })
  const { data: customers } = useQuery<CustomerSummary[]>({
    queryKey: ['platform-admin-customers', customerSearch],
    queryFn: () => platformAdminApi.get('/platform-admin/customers', { params: { search: customerSearch || undefined } }).then((r) => r.data),
  })

  return (
    <div className="min-h-screen bg-gray-950 text-white p-6">
      <div className="max-w-5xl mx-auto">
        <div className="flex items-center justify-between mb-8">
          <h1 className="text-2xl font-bold">Platform Admin</h1>
          <div className="flex items-center gap-4 text-sm text-gray-400">
            <span>{user?.name}</span>
            <button onClick={logout} className="hover:text-white transition-colors">Sign out</button>
          </div>
        </div>

        <div className="grid md:grid-cols-2 gap-6">
          <section className="bg-gray-900 border border-gray-800 rounded-2xl p-5">
            <h2 className="font-semibold mb-3">Business owners</h2>
            <input
              value={barberSearch} onChange={(e) => setBarberSearch(e.target.value)}
              placeholder="Search by name, email, or URL"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <div className="space-y-1 max-h-96 overflow-y-auto">
              {barbers?.map((b) => (
                <Link key={b.id} to={`/platform-admin/barbers/${b.id}`}
                  className="flex items-center justify-between px-3 py-2.5 rounded-lg hover:bg-gray-800 transition-colors">
                  <div>
                    <div className="text-sm font-medium">{b.name}</div>
                    <div className="text-xs text-gray-500">{b.email} · /{b.slug}</div>
                  </div>
                  <span className={`text-xs px-2 py-0.5 rounded-full ${
                    b.subscriptionStatus === 'ACTIVE' ? 'bg-green-900/40 text-green-300'
                      : b.subscriptionStatus === 'TRIAL' ? 'bg-blue-900/40 text-blue-300' : 'bg-red-900/40 text-red-300'
                  }`}>{b.subscriptionStatus}</span>
                </Link>
              ))}
              {barbers?.length === 0 && <p className="text-gray-500 text-sm px-3 py-2">No results</p>}
            </div>
          </section>

          <section className="bg-gray-900 border border-gray-800 rounded-2xl p-5">
            <h2 className="font-semibold mb-3">Customers</h2>
            <input
              value={customerSearch} onChange={(e) => setCustomerSearch(e.target.value)}
              placeholder="Search by name or phone"
              className="w-full bg-gray-800 border border-gray-700 rounded-lg px-3 py-2 text-sm mb-3 focus:outline-none focus:ring-2 focus:ring-blue-500"
            />
            <div className="space-y-1 max-h-96 overflow-y-auto">
              {customers?.map((c) => (
                <Link key={c.id} to={`/platform-admin/customers/${c.id}`}
                  className="flex items-center justify-between px-3 py-2.5 rounded-lg hover:bg-gray-800 transition-colors">
                  <div>
                    <div className="text-sm font-medium">{c.name} {c.familyName}</div>
                    <div className="text-xs text-gray-500">{c.phone}</div>
                  </div>
                </Link>
              ))}
              {customers?.length === 0 && <p className="text-gray-500 text-sm px-3 py-2">No results</p>}
            </div>
          </section>
        </div>
      </div>
    </div>
  )
}
