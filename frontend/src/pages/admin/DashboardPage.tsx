import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { addDays, startOfWeek, format } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import WeeklyCalendar from '../../components/admin/WeeklyCalendar'

export default function DashboardPage() {
  const [weekOffset, setWeekOffset] = useState(0)
  const { language: lang } = useAuth()

  const { data: appointments = [] } = useQuery({
    queryKey: ['dashboard', weekOffset],
    queryFn: () => api.get(`/admin/dashboard?week=${weekOffset}`).then((r) => r.data),
  })

  const weekStart = startOfWeek(addDays(new Date(), weekOffset * 7), { weekStartsOn: 0 })

  return (
    <div>
      <h1 className="text-2xl font-bold text-white mb-6">{t(lang, 'dashboard')}</h1>
      <WeeklyCalendar
        appointments={appointments}
        weekStart={format(weekStart, "yyyy-MM-dd'T'HH:mm:ss.SSS'Z'")}
        weekOffset={weekOffset}
        onWeekChange={setWeekOffset}
        lang={lang}
      />
    </div>
  )
}
