import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useCustomerAuth } from '../../lib/customerAuth'
import { customerApi } from '../../lib/customerApi'
import { t, type TKey } from '../../lib/i18n'
import CustomerAccountNav from '../../components/customer/CustomerAccountNav'
import BackButton from '../../components/BackButton'
import AppointmentCard, { type Appointment } from '../../components/customer/AppointmentCard'

const FILTERS: { value: string; key: TKey }[] = [
  { value: 'upcoming', key: 'upcoming' },
  { value: 'today', key: 'today' },
  { value: 'past', key: 'past' },
  { value: 'all', key: 'all' },
]

export default function MyBookingsPage() {
  const { language: lang } = useCustomerAuth()
  const queryClient = useQueryClient()
  const [filter, setFilter] = useState('upcoming')

  const { data: appointments = [], isLoading } = useQuery<Appointment[]>({
    queryKey: ['my-bookings', filter],
    queryFn: () => customerApi.get(`/customer/appointments?filter=${filter}`).then((r) => r.data),
  })

  function invalidate() {
    queryClient.invalidateQueries({ queryKey: ['my-bookings'] })
  }

  const emptyKey: TKey = filter === 'past' ? 'noPastBookings' : 'noUpcomingBookings'

  return (
    <div className="min-h-screen bg-gray-950 text-white">
      <CustomerAccountNav />
      <div className="max-w-2xl mx-auto px-4 py-8">
        <BackButton lang={lang} />
        <h1 className="text-2xl font-bold text-white mb-6 mt-3">{t(lang, 'myBookingsTitle')}</h1>

        <div className="flex gap-1 bg-gray-900 rounded-xl p-1 border border-gray-800 mb-6 w-fit">
          {FILTERS.map((f) => (
            <button key={f.value} onClick={() => setFilter(f.value)}
              className={`px-4 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                filter === f.value ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white'
              }`}>{t(lang, f.key)}
            </button>
          ))}
        </div>

        {isLoading ? (
          <div className="text-center text-gray-500 py-12">{t(lang, 'loading')}</div>
        ) : appointments.length === 0 ? (
          <div className="text-center text-gray-500 py-12">{t(lang, emptyKey)}</div>
        ) : (
          <div className="space-y-3">
            {appointments.map((appt) => (
              <AppointmentCard key={appt.id} appt={appt} lang={lang} onChanged={invalidate} />
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
