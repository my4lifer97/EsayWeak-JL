import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCustomerAuth } from '../../lib/customerAuth'
import { customerApi } from '../../lib/customerApi'
import { t } from '../../lib/i18n'
import CustomerAccountNav from '../../components/customer/CustomerAccountNav'
import BackButton from '../../components/BackButton'

type BarberResult = { slug: string; name: string; description: string | null; logo: string | null }

export default function FollowedBarbersPage() {
  const { language: lang } = useCustomerAuth()
  const queryClient = useQueryClient()
  const [pending, setPending] = useState<string | null>(null)

  const { data: barbers = [], isLoading } = useQuery<BarberResult[]>({
    queryKey: ['followed-barbers'],
    queryFn: () => customerApi.get('/barbers/followed').then((r) => r.data),
  })

  async function unfollow(slug: string) {
    setPending(slug)
    try {
      await customerApi.delete(`/barbers/${slug}/follow`)
      queryClient.invalidateQueries({ queryKey: ['followed-barbers'] })
      queryClient.invalidateQueries({ queryKey: ['barber-search'] })
    } finally { setPending(null) }
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white">
      <CustomerAccountNav />
      <div className="max-w-2xl mx-auto px-4 py-8">
        <BackButton lang={lang} />
        <h1 className="text-2xl font-bold text-white mb-6 mt-3">{t(lang, 'followedBarbers')}</h1>

        {isLoading ? (
          <div className="text-center text-gray-500 py-12">{t(lang, 'loading')}</div>
        ) : barbers.length === 0 ? (
          <div className="text-center text-gray-500 py-12">
            <p className="mb-4">{t(lang, 'noFollowedYet')}</p>
            <Link to="/browse" className="text-blue-400 hover:text-blue-300 text-sm font-medium">{t(lang, 'browseBarbers')}</Link>
          </div>
        ) : (
          <div className="space-y-3">
            {barbers.map((b) => (
              <div key={b.slug} className="bg-gray-900 border border-gray-800 rounded-2xl p-4 flex items-center justify-between gap-3">
                <div className="flex-1 min-w-0">
                  <div className="font-semibold text-white truncate">{b.name}</div>
                  {b.description && <div className="text-gray-500 text-sm truncate mt-0.5">{b.description}</div>}
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  <Link to={`/${b.slug}`} className="text-sm text-blue-400 hover:text-blue-300 font-medium px-3 py-2">
                    {t(lang, 'viewBarberPage')}
                  </Link>
                  <button
                    disabled={pending === b.slug}
                    onClick={() => unfollow(b.slug)}
                    className="text-sm text-gray-400 hover:text-red-400 font-medium px-3 py-2 disabled:opacity-50">
                    {t(lang, 'unfollowBarber')}
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
