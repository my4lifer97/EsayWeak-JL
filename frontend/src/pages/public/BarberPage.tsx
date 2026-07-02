import { useState } from 'react'
import { useParams, useNavigate, Link } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'

type BarberInfo = {
  slug: string; name: string; description: string | null; logo: string | null
  language: string; isRTL: boolean; activeDays: number[]
  services: { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number }[]
  isFollowed: boolean
}

export default function BarberPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { isAuthenticated } = useCustomerAuth()
  const [followLoading, setFollowLoading] = useState(false)

  const { data: barber, isLoading } = useQuery<BarberInfo>({
    queryKey: ['barber', slug],
    queryFn: () => customerApi.get(`/${slug}/info`).then((r) => r.data),
  })

  const lang = barber?.language ?? 'EN'
  const dir = barber?.isRTL ? 'rtl' : 'ltr'

  async function toggleFollow() {
    if (!isAuthenticated) { navigate(`/login?next=/${slug}`); return }
    setFollowLoading(true)
    try {
      if (barber?.isFollowed) await customerApi.delete(`/barbers/${slug}/follow`)
      else await customerApi.post(`/barbers/${slug}/follow`)
      queryClient.invalidateQueries({ queryKey: ['barber', slug] })
    } finally { setFollowLoading(false) }
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <div className="text-gray-500">{t('EN', 'loading')}</div>
      </div>
    )
  }
  if (!barber) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <div className="text-gray-400">{t('EN', 'barberNotFound')}</div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white" dir={dir}>
      <div className="max-w-lg mx-auto px-4 py-10">
        <div className="text-center mb-8">
          <div className="text-5xl mb-4">✂️</div>
          <h1 className="text-3xl font-bold text-white">{barber.name}</h1>
          {barber.description && (
            <p className="text-gray-400 mt-2 text-sm">{barber.description}</p>
          )}
        </div>

        <div className="space-y-3">
          <button onClick={() => navigate(`/${slug}/book`)}
            className="flex items-center justify-center gap-2 w-full bg-blue-600 hover:bg-blue-700 text-white font-bold py-4 rounded-2xl transition-colors">
            ✂️ {t(lang, 'bookAppointment')}
          </button>

          <button
            disabled={followLoading}
            onClick={toggleFollow}
            className={`w-full font-semibold py-3 rounded-2xl transition-colors disabled:opacity-50 ${
              barber.isFollowed
                ? 'bg-gray-800 text-gray-300 border border-gray-700 hover:bg-gray-700'
                : 'bg-gray-900 border border-blue-600/40 text-blue-400 hover:bg-blue-900/20'
            }`}>
            {barber.isFollowed ? t(lang, 'following') : t(lang, 'followBarber')}
          </button>

          {isAuthenticated && (
            <Link to="/account/bookings"
              className="block text-center text-gray-500 hover:text-gray-300 text-sm py-2 transition-colors">
              {t(lang, 'viewMyAppointments')}
            </Link>
          )}
        </div>
      </div>
    </div>
  )
}
