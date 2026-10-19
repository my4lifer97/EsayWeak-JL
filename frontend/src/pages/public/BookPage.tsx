import { useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'
import BookingWizard from '../../components/booking/BookingWizard'
import NotFoundPage from '../NotFoundPage'
import RouteErrorPage from '../../components/RouteErrorPage'

type BusinessInfo = {
  slug: string; name: string; description: string | null; logo: string | null
  language: string; isRTL: boolean; activeDays: number[]; openDates?: string[]
  items: {
    id: string; nameEn: string; nameAr: string; nameHe: string; durationMinutes: number | null; price: number | null
    photoMode: 'None' | 'OwnerGallery' | 'CustomerUpload' | 'Both'; isBookable: boolean; galleryPhotos: { id: string; url: string }[]
  }[]
  waitlistEnabled: boolean
}

export default function BookPage() {
  const { slug } = useParams<{ slug: string }>()
  const { language: lang } = useCustomerAuth()
  const { data: business, isLoading, error: businessError } = useQuery<BusinessInfo>({
    queryKey: ['business', slug],
    queryFn: () => customerApi.get(`/${slug}/info`).then((r) => r.data),
  })

  if (isLoading) return <div className="min-h-screen bg-cream flex items-center justify-center"><div className="text-muted">{t(lang, 'loading')}</div></div>
  // Same as BusinessPage: the 404 page for an unknown slug, a reload page for anything else.
  if (!business) return (businessError as { response?: { status?: number } } | null)?.response?.status === 404 ? <NotFoundPage /> : <RouteErrorPage />

  return <BookingWizard business={business} />
}
