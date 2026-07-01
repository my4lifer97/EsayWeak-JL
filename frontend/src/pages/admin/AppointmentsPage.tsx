import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t, type TKey } from '../../lib/i18n'

type Appointment = {
  id: string; date: string; startTime: string; endTime: string
  status: string; notes: string | null
  customer: { name: string; phone: string }
  service: { nameEn: string }
  price: number
}

const FILTERS: { value: string; key: TKey }[] = [
  { value: 'upcoming', key: 'upcoming' },
  { value: 'today', key: 'today' },
  { value: 'past', key: 'past' },
  { value: 'all', key: 'all' },
]
const STATUS_BADGE: Record<string, string> = {
  CONFIRMED: 'bg-blue-900/50 text-blue-300',
  COMPLETED: 'bg-green-900/50 text-green-300',
  CANCELLED: 'bg-gray-700/50 text-gray-400',
}

export default function AppointmentsPage() {
  const { language: lang } = useAuth()
  const [filter, setFilter] = useState<string>('upcoming')
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState<string | null>(null)
  const queryClient = useQueryClient()

  const { data: appointments = [] } = useQuery<Appointment[]>({
    queryKey: ['appointments', filter],
    queryFn: () => api.get(`/admin/appointments?filter=${filter}`).then((r) => r.data),
  })

  const STATUS_LABEL: Record<string, TKey> = {
    CONFIRMED: 'statusConfirmed',
    COMPLETED: 'statusCompleted',
    CANCELLED: 'statusCancelled',
  }

  const filtered = appointments.filter((a) =>
    !search || a.customer.name.toLowerCase().includes(search.toLowerCase()) || a.customer.phone.includes(search)
  )

  async function updateStatus(id: string, status: string) {
    setLoading(id)
    await api.patch(`/admin/appointments/${id}`, { status })
    setLoading(null)
    queryClient.invalidateQueries({ queryKey: ['appointments'] })
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-white mb-6">{t(lang, 'appointments')}</h1>
      <div className="flex flex-wrap gap-3 mb-4 items-center">
        <div className="flex gap-1 bg-gray-900 rounded-xl p-1 border border-gray-800">
          {FILTERS.map((f) => (
            <button key={f.value} onClick={() => setFilter(f.value)}
              className={`px-4 py-1.5 rounded-lg text-sm font-medium transition-colors ${
                filter === f.value ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white'
              }`}>{t(lang, f.key)}
            </button>
          ))}
        </div>
        <input type="text" placeholder={t(lang, 'searchPlaceholder')} value={search} onChange={(e) => setSearch(e.target.value)}
          className="bg-gray-900 border border-gray-700 rounded-lg px-3 py-1.5 text-white text-sm placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500" />
      </div>

      {filtered.length === 0 ? (
        <div className="text-center text-gray-500 py-16">{t(lang, 'noAppointments')}</div>
      ) : (
        <div className="bg-gray-900 rounded-2xl border border-gray-800 overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                {(['date', 'time', 'colCustomer', 'phone', 'service', 'status', 'colActions'] as TKey[]).map((key) => (
                  <th key={key} className="px-4 py-3 font-medium">{t(lang, key)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filtered.map((a) => (
                <tr key={a.id} className="border-b border-gray-800/50 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white">{format(parseISO(a.date.slice(0, 10)), 'MMM d, yyyy')}</td>
                  <td className="px-4 py-3 text-gray-300">{a.startTime}–{a.endTime}</td>
                  <td className="px-4 py-3 text-white font-medium">{a.customer.name}</td>
                  <td className="px-4 py-3 text-gray-300">{a.customer.phone}</td>
                  <td className="px-4 py-3 text-gray-300">{a.service.nameEn}</td>
                  <td className="px-4 py-3">
                    <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_BADGE[a.status] ?? 'bg-gray-700 text-gray-300'}`}>
                      {STATUS_LABEL[a.status] ? t(lang, STATUS_LABEL[a.status]) : a.status}
                    </span>
                  </td>
                  <td className="px-4 py-3">
                    {a.status === 'CONFIRMED' && (
                      <div className="flex gap-2">
                        <button disabled={loading === a.id} onClick={() => updateStatus(a.id, 'COMPLETED')}
                          className="text-xs text-green-400 hover:text-green-300 disabled:opacity-50">{t(lang, 'complete')}</button>
                        <button disabled={loading === a.id} onClick={() => updateStatus(a.id, 'CANCELLED')}
                          className="text-xs text-red-400 hover:text-red-300 disabled:opacity-50">{t(lang, 'cancel')}</button>
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}
