import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t, serviceName, type TKey } from '../../lib/i18n'
import { mediaUrl } from '../../lib/media'
import NewAppointmentModal from '../../components/admin/NewAppointmentModal'
import CancelOptionsModal from '../../components/admin/CancelOptionsModal'

type Appointment = {
  id: string; date: string; startTime: string; endTime: string
  status: string; notes: string | null
  customer: { name: string; familyName: string; phone: string }
  service: { id: string; nameEn: string; nameAr: string; nameHe: string }
  price: number
  photoUrl: string | null
  recurringSeriesId: string | null
  pendingCancellationApproval: boolean
}
type Service = { id: string; nameEn: string; nameAr: string; nameHe: string }

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
const STATUS_OPTIONS: TKey[] = ['statusConfirmed', 'statusCompleted', 'statusCancelled']
const STATUS_VALUES = ['CONFIRMED', 'COMPLETED', 'CANCELLED']

export default function AppointmentsPage() {
  const { language: lang } = useAuth()
  const [filter, setFilter] = useState<string>('upcoming')
  const [search, setSearch] = useState('')
  const [serviceFilter, setServiceFilter] = useState('')
  const [statusFilter, setStatusFilter] = useState('')
  const [typeFilter, setTypeFilter] = useState<'all' | 'recurring' | 'onetime'>('all')
  const [showNewAppointment, setShowNewAppointment] = useState(false)
  const [cancelTargetId, setCancelTargetId] = useState<string | null>(null)
  const queryClient = useQueryClient()

  const { data: appointments = [] } = useQuery<Appointment[]>({
    queryKey: ['appointments', filter],
    queryFn: () => api.get(`/admin/appointments?filter=${filter}`).then((r) => r.data),
  })

  const { data: services = [] } = useQuery<Service[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/services').then((r) => r.data),
  })

  const { data: settings } = useQuery<{ waitlistEnabled: boolean }>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  const STATUS_LABEL: Record<string, TKey> = {
    CONFIRMED: 'statusConfirmed',
    COMPLETED: 'statusCompleted',
    CANCELLED: 'statusCancelled',
  }

  const filtered = appointments.filter((a) => {
    // Match against the concatenated "First Last" rather than just customer.name -- the search
    // box previously never checked familyName at all, so searching a customer's last name (or
    // their full name) matched nothing even though they were right there in the list.
    const fullName = `${a.customer.name} ${a.customer.familyName}`.toLowerCase()
    if (search && !fullName.includes(search.toLowerCase()) && !a.customer.phone.includes(search)) return false
    if (serviceFilter && a.service.id !== serviceFilter) return false
    if (statusFilter && a.status !== statusFilter) return false
    if (typeFilter === 'recurring' && !a.recurringSeriesId) return false
    if (typeFilter === 'onetime' && a.recurringSeriesId) return false
    return true
  })

  const hasActiveFilters = !!search || !!serviceFilter || !!statusFilter || typeFilter !== 'all'
  function clearFilters() {
    setSearch(''); setServiceFilter(''); setStatusFilter(''); setTypeFilter('all')
  }

  function onCancelFlowDone() {
    setCancelTargetId(null)
    queryClient.invalidateQueries({ queryKey: ['appointments'] })
  }

  const selectClass = 'bg-gray-900 border border-gray-700 rounded-lg px-3 py-1.5 text-white text-sm focus:outline-none focus:ring-2 focus:ring-blue-500'

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-white">{t(lang, 'appointments')}</h1>
        <button onClick={() => setShowNewAppointment(true)}
          className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
          {t(lang, 'newAppointment')}
        </button>
      </div>

      <div className="flex flex-wrap gap-3 mb-3 items-center">
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

      <div className="flex flex-wrap gap-3 mb-4 items-center">
        <select value={serviceFilter} onChange={(e) => setServiceFilter(e.target.value)} className={selectClass}>
          <option value="">{t(lang, 'allServices')}</option>
          {services.map((s) => <option key={s.id} value={s.id}>{serviceName(s, lang)}</option>)}
        </select>
        <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)} className={selectClass}>
          <option value="">{t(lang, 'allStatuses')}</option>
          {STATUS_OPTIONS.map((key, i) => <option key={key} value={STATUS_VALUES[i]}>{t(lang, key)}</option>)}
        </select>
        <select value={typeFilter} onChange={(e) => setTypeFilter(e.target.value as typeof typeFilter)} className={selectClass}>
          <option value="all">{t(lang, 'allTypes')}</option>
          <option value="recurring">{t(lang, 'recurringOnly')}</option>
          <option value="onetime">{t(lang, 'oneTimeOnly')}</option>
        </select>
        {hasActiveFilters && (
          <button onClick={clearFilters} className="text-sm text-blue-400 hover:text-blue-300">{t(lang, 'clearFilters')}</button>
        )}
        <span className="text-gray-500 text-xs ms-auto">{filtered.length} / {appointments.length} {t(lang, 'appointments')}</span>
      </div>

      {filtered.length === 0 ? (
        <div className="text-center text-gray-500 py-16">{t(lang, 'noAppointments')}</div>
      ) : (
        <div className="bg-gray-900 rounded-2xl border border-gray-800 overflow-x-auto">
          <table className="w-full text-sm min-w-[720px]">
            <thead>
              <tr className="border-b border-gray-800 text-gray-400 text-left">
                {(['date', 'time', 'colCustomer', 'phone', 'service', 'referencePhoto', 'status', 'colActions'] as TKey[]).map((key) => (
                  <th key={key} className="px-4 py-3 font-medium">{t(lang, key)}</th>
                ))}
              </tr>
            </thead>
            <tbody>
              {filtered.map((a) => (
                <tr key={a.id} className="border-b border-gray-800/50 hover:bg-gray-800/30">
                  <td className="px-4 py-3 text-white">{format(parseISO(a.date.slice(0, 10)), 'MMM d, yyyy')}</td>
                  <td className="px-4 py-3 text-gray-300">{a.startTime}–{a.endTime}</td>
                  <td className="px-4 py-3 text-white font-medium">
                    {a.recurringSeriesId && <span title={t(lang, 'partOfSeries')}>🔁 </span>}{a.customer.name}
                  </td>
                  <td className="px-4 py-3 text-gray-300">{a.customer.phone}</td>
                  <td className="px-4 py-3 text-gray-300">{serviceName(a.service, lang)}</td>
                  <td className="px-4 py-3">
                    {a.photoUrl && (
                      <a href={mediaUrl(a.photoUrl)} target="_blank" rel="noreferrer">
                        <img src={mediaUrl(a.photoUrl)} alt={t(lang, 'referencePhoto')} className="w-10 h-10 object-cover rounded-lg border border-gray-700 hover:border-blue-500 transition-colors" />
                      </a>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    {a.pendingCancellationApproval ? (
                      <span className="px-2 py-0.5 rounded-full text-xs font-medium bg-amber-900/50 text-amber-300">
                        ⚠️ {t(lang, 'cancellationRequestedBadge')}
                      </span>
                    ) : (
                      <span className={`px-2 py-0.5 rounded-full text-xs font-medium ${STATUS_BADGE[a.status] ?? 'bg-gray-700 text-gray-300'}`}>
                        {STATUS_LABEL[a.status] ? t(lang, STATUS_LABEL[a.status]) : a.status}
                      </span>
                    )}
                  </td>
                  <td className="px-4 py-3">
                    {a.status === 'CONFIRMED' && (
                      <button onClick={() => setCancelTargetId(a.id)}
                        className={`text-xs ${a.pendingCancellationApproval ? 'text-amber-400 hover:text-amber-300 font-semibold' : 'text-red-400 hover:text-red-300'}`}>
                        {a.pendingCancellationApproval ? t(lang, 'resolveCancellationRequest') : t(lang, 'cancel')}
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {showNewAppointment && (
        <NewAppointmentModal lang={lang} onClose={() => setShowNewAppointment(false)} />
      )}
      {cancelTargetId && (
        <CancelOptionsModal
          lang={lang}
          appointmentId={cancelTargetId}
          waitlistEnabled={settings?.waitlistEnabled ?? false}
          onClose={() => setCancelTargetId(null)}
          onDone={onCancelFlowDone}
        />
      )}
    </div>
  )
}
