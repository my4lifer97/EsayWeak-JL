import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'
import BookingWizard from '../../components/booking/BookingWizard'

type BarberInfo = {
  slug: string; name: string; description: string | null; logo: string | null
  language: string; isRTL: boolean; activeDays: number[]
  services: { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number }[]
}

export default function BookPage() {
  const { slug } = useParams<{ slug: string }>()
  const { language: lang } = useCustomerAuth()
  const { data: barber, isLoading } = useQuery<BarberInfo>({
    queryKey: ['barber', slug],
    queryFn: () => customerApi.get(`/${slug}/info`).then((r) => r.data),
  })

  if (isLoading) return <div className="min-h-screen bg-gray-950 flex items-center justify-center"><div className="text-gray-500">{t(lang, 'loading')}</div></div>
  if (!barber) return <div className="min-h-screen bg-gray-950 flex items-center justify-center"><div className="text-gray-400">{t(lang, 'barberNotFound')}</div></div>

  return <BookingWizard barber={barber} />
}
