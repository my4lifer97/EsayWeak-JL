import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuth } from '../../lib/auth'
import { useCustomerAuth } from '../../lib/customerAuth'
import { customerApi } from '../../lib/customerApi'
import { t } from '../../lib/i18n'
import CustomerAccountNav from '../../components/customer/CustomerAccountNav'

type BarberResult = { slug: string; name: string; description: string | null; logo: string | null; isFollowed: boolean }

export default function BrowseBarbersPage() {
  const { language: lang } = useAuth()
  const { isAuthenticated } = useCustomerAuth()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [query, setQuery] = useState('')
  const [pending, setPending] = useState<string | null>(null)

  const { data: barbers = [], isLoading } = useQuery<BarberResult[]>({
    queryKey: ['barber-search', query],
    queryFn: () => customerApi.get(`/barbers/search?query=${encodeURIComponent(query)}`).then((r) => r.data),
  })

  async function toggleFollow(barber: BarberResult) {
    if (!isAuthenticated) { navigate('/login?next=/browse'); return }
    setPending(barber.slug)
    try {
      if (barber.isFollowed) await customerApi.delete(`/barbers/${barber.slug}/follow`)
      else await customerApi.post(`/barbers/${barber.slug}/follow`)
      queryClient.invalidateQueries({ queryKey: ['barber-search'] })
      queryClient.invalidateQueries({ queryKey: ['followed-barbers'] })
    } finally { setPending(null) }
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white">
      <CustomerAccountNav />
      <div className="max-w-2xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold text-white mb-6">{t(lang, 'browseBarbers')}</h1>

        <input
          type="text" value={query} onChange={(e) => setQuery(e.target.value)}
          placeholder={t(lang, 'searchBarbersPlaceholder')}
          className="w-full bg-gray-900 border border-gray-700 rounded-xl px-4 py-3 text-white placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 mb-6"
        />

        {isLoading ? (
          <div className="text-center text-gray-500 py-12">{t(lang, 'loading')}</div>
        ) : barbers.length === 0 ? (
          <div className="text-center text-gray-500 py-12">{t(lang, 'noBarbersFound')}</div>
        ) : (
          <div className="space-y-3">
            {barbers.map((b) => (
              <div key={b.slug} className="bg-gray-900 border border-gray-800 rounded-2xl p-4 flex items-center justify-between gap-3">
                <Link to={`/${b.slug}`} className="flex-1 min-w-0">
                  <div className="font-semibold text-white truncate">{b.name}</div>
                  {b.description && <div className="text-gray-500 text-sm truncate mt-0.5">{b.description}</div>}
                </Link>
                <button
                  disabled={pending === b.slug}
                  onClick={() => toggleFollow(b)}
                  className={`shrink-0 text-sm font-medium px-4 py-2 rounded-xl transition-colors disabled:opacity-50 ${
                    b.isFollowed
                      ? 'bg-gray-800 text-gray-300 border border-gray-700 hover:bg-gray-700'
                      : 'bg-blue-600 hover:bg-blue-700 text-white'
                  }`}>
                  {b.isFollowed ? t(lang, 'following') : t(lang, 'followBarber')}
                </button>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
