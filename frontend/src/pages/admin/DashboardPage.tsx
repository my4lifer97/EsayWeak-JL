import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { addDays, startOfWeek, format } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import WeeklyCalendar from '../../components/admin/WeeklyCalendar'
import NewAppointmentModal from '../../components/admin/NewAppointmentModal'

export default function DashboardPage() {
  const [weekOffset, setWeekOffset] = useState(0)
  const [showNewAppointment, setShowNewAppointment] = useState(false)
  const { language: lang } = useAuth()

  const { data: appointments = [] } = useQuery({
    queryKey: ['dashboard', weekOffset],
    queryFn: () => api.get(`/admin/dashboard?week=${weekOffset}`).then((r) => r.data),
  })

  const weekStart = startOfWeek(addDays(new Date(), weekOffset * 7), { weekStartsOn: 0 })

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-white">{t(lang, 'dashboard')}</h1>
        <button onClick={() => setShowNewAppointment(true)}
          className="bg-blue-600 hover:bg-blue-700 text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
          {t(lang, 'newAppointment')}
        </button>
      </div>
      <WeeklyCalendar
        appointments={appointments}
        weekStart={format(weekStart, "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'")}
        weekOffset={weekOffset}
        onWeekChange={setWeekOffset}
        lang={lang}
      />
      {showNewAppointment && (
        <NewAppointmentModal lang={lang} defaultDate={format(new Date(), 'yyyy-MM-dd')} onClose={() => setShowNewAppointment(false)} />
      )}
    </div>
  )
}
