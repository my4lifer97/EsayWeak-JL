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
  // null = closed; date/time set when the owner clicked a spot on the calendar.
  const [newAppointment, setNewAppointment] = useState<{ date: string; time?: string } | null>(null)
  const { language: lang } = useAuth()

  const { data: appointments = [] } = useQuery({
    queryKey: ['dashboard', weekOffset],
    queryFn: () => api.get(`/admin/dashboard?week=${weekOffset}`).then((r) => r.data),
  })

  const weekStart = startOfWeek(addDays(new Date(), weekOffset * 7), { weekStartsOn: 0 })

  return (
    <div>
      <div className="flex justify-between items-center mb-6">
        <h1 className="text-2xl font-bold text-ink">{t(lang, 'dashboard')}</h1>
        <button onClick={() => setNewAppointment({ date: format(new Date(), 'yyyy-MM-dd') })}
          className="bg-coral hover:bg-coral-dark text-white text-sm font-medium px-4 py-2 rounded-lg transition-colors">
          {t(lang, 'newAppointment')}
        </button>
      </div>
      <WeeklyCalendar
        appointments={appointments}
        weekStart={format(weekStart, "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'")}
        weekOffset={weekOffset}
        onWeekChange={setWeekOffset}
        lang={lang}
        onSlotClick={(date, time) => setNewAppointment({ date, time })}
      />
      {newAppointment && (
        <NewAppointmentModal lang={lang} defaultDate={newAppointment.date} defaultTime={newAppointment.time}
          onClose={() => setNewAppointment(null)} />
      )}
    </div>
  )
}
