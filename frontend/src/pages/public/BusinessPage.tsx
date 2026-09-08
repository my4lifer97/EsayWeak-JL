import { useState } from 'react'
import { useParams, useNavigate, useLocation } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t, itemName } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'
import BackButton from '../../components/BackButton'
import LanguageSwitcher from '../../components/customer/LanguageSwitcher'
import AppointmentCard, { type Appointment } from '../../components/customer/AppointmentCard'
import BusinessReviews from '../../components/customer/BusinessReviews'

type BusinessInfo = {
  slug: string; name: string; description: string | null; logo: string | null
  language: string; isRTL: boolean; activeDays: number[]
  items: { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number | null; price: number | null; isBookable: boolean }[]
  isFollowed: boolean
}

export default function BusinessPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { isAuthenticated, language: lang } = useCustomerAuth()
  const [followLoading, setFollowLoading] = useState(false)
  const [showAppointments, setShowAppointments] = useState(false)

  const { data: business, isLoading } = useQuery<BusinessInfo>({
    queryKey: ['business', slug],
    queryFn: () => customerApi.get(`/${slug}/info`).then((r) => r.data),
  })

  const { data: myAppointments = [] } = useQuery<Appointment[]>({
    queryKey: ['business-appointments', slug],
    queryFn: () => customerApi.get(`/customer/appointments?filter=all&businessSlug=${slug}`).then((r) => r.data),
    enabled: isAuthenticated && !!slug,
  })
  const activeAppointments = myAppointments.filter((a) => a.status === 'CONFIRMED')

  function invalidateAppointments() {
    queryClient.invalidateQueries({ queryKey: ['business-appointments', slug] })
  }

  // The customer's own language choice drives the UI everywhere, overriding this specific
  // business's configured storefront language (confirmed default behavior for this app).
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'

  async function toggleFollow() {
    if (!isAuthenticated) { navigate(`/login?next=/${slug}`); return }
    setFollowLoading(true)
    try {
      if (business?.isFollowed) await customerApi.delete(`/businesses/${slug}/follow`)
      else await customerApi.post(`/businesses/${slug}/follow`)
      queryClient.invalidateQueries({ queryKey: ['business', slug] })
    } finally { setFollowLoading(false) }
  }

  if (isLoading) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <div className="text-gray-500">{t(lang, 'loading')}</div>
      </div>
    )
  }
  if (!business) {
    return (
      <div className="min-h-screen bg-gray-950 flex items-center justify-center">
        <div className="text-gray-400">{t(lang, 'businessNotFound')}</div>
      </div>
    )
  }

  return (
    <div className="min-h-screen bg-gray-950 text-white" dir={dir}>
      <div className="max-w-lg mx-auto px-4 py-10">
        <div className="flex items-center justify-between mb-6">
          <BackButton lang={lang} />
          <LanguageSwitcher />
        </div>
        <div className="text-center mb-8">
          {business.logo ? (
            <img src={mediaUrl(business.logo)} alt={business.name}
              className="w-24 h-24 rounded-full object-cover mx-auto mb-4 border border-gray-800" />
          ) : (
            <div className="text-5xl mb-4">✂️</div>
          )}
          <h1 className="text-3xl font-bold text-white">{business.name}</h1>
          {business.description && (
            <p className="text-gray-400 mt-2 text-sm">{business.description}</p>
          )}
        </div>

        <div className="space-y-3">
          {business.items.some((i) => i.isBookable) && (
            <button onClick={() => navigate(`/${slug}/book`)}
              className="flex items-center justify-center gap-2 w-full bg-blue-600 hover:bg-blue-700 text-white font-bold py-4 rounded-2xl transition-colors">
              ✂️ {t(lang, 'bookAppointment')}
            </button>
          )}

          <button
            disabled={followLoading}
            onClick={toggleFollow}
            className={`w-full font-semibold py-3 rounded-2xl transition-colors disabled:opacity-50 ${
              business.isFollowed
                ? 'bg-gray-800 text-gray-300 border border-gray-700 hover:bg-gray-700'
                : 'bg-gray-900 border border-blue-600/40 text-blue-400 hover:bg-blue-900/20'
            }`}>
            {business.isFollowed ? t(lang, 'following') : t(lang, 'followBusiness')}
          </button>

          {activeAppointments.length > 0 && (
            <button
              onClick={() => setShowAppointments(!showAppointments)}
              className="w-full font-semibold py-3 rounded-2xl border border-gray-700 text-gray-300 hover:bg-gray-800 transition-colors">
              {t(lang, 'myBookingsWithBusiness')}
            </button>
          )}

        </div>

        {showAppointments && activeAppointments.length > 0 && (
          <div className="space-y-3 mt-4">
            {activeAppointments.map((appt) => (
              <AppointmentCard key={appt.id} appt={appt} lang={lang} showBusinessName={false} onChanged={invalidateAppointments} />
            ))}
          </div>
        )}

        {/* Showcase items -- no booking flow, just what the business offers. */}
        {business.items.some((i) => !i.isBookable) && (
          <div className="space-y-2 mt-6">
            {business.items.filter((i) => !i.isBookable).map((i) => (
              <div key={i.id} className="bg-gray-900 border border-gray-800 rounded-xl px-4 py-3 flex justify-between items-center">
                <span className="text-white text-sm">{itemName(i, lang)}</span>
                {i.price !== null && <span className="text-blue-400 text-sm font-semibold">₪{Number(i.price).toFixed(0)}</span>}
              </div>
            ))}
          </div>
        )}

        <BusinessReviews
          slug={slug!}
          lang={lang}
          isAuthenticated={isAuthenticated}
          openForm={location.hash === '#reviews'}
        />
      </div>
    </div>
  )
}
