import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import BookingWizard from '../../components/booking/BookingWizard'

type BarberInfo = {
  slug: string; name: string; description: string | null; logo: string | null
  language: string; isRTL: boolean; activeDays: number[]
  services: { id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number; price: number }[]
}

export default function BookPage() {
  const { slug } = useParams<{ slug: string }>()
  const { data: barber, isLoading } = useQuery<BarberInfo>({
    queryKey: ['barber', slug],
    queryFn: () => api.get(`/${slug}/info`).then((r) => r.data),
  })

  if (isLoading) return <div className="min-h-screen bg-gray-950 flex items-center justify-center"><div className="text-gray-500">Loading...</div></div>
  if (!barber) return <div className="min-h-screen bg-gray-950 flex items-center justify-center"><div className="text-gray-400">Not found</div></div>

  return <BookingWizard barber={barber} />
}
