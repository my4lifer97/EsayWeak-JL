import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { format, parseISO } from 'date-fns'
import { platformAdminApi } from '../../lib/platformAdminApi'

type PaidPeriodEnded = { id: string; name: string; slug: string; paidUntil: string }

// Businesses whose paid period has ended and that nobody has renewed or disabled yet. The backend
// also emails the admin once for each (PaidPeriodMonitor); this stays until it's dealt with.
export default function PaidPeriodEndedBanner() {
  const { data } = useQuery<PaidPeriodEnded[]>({
    queryKey: ['platform-admin-paid-period-ended'],
    queryFn: () => platformAdminApi.get('/platform-admin/paid-period-ended').then((r) => r.data),
  })
  if (!data?.length) return null

  return (
    <div role="alert" className="mb-6 rounded-2xl border border-amber-300 bg-amber-50 text-amber-900 dark:border-amber-800 dark:bg-amber-950/40 dark:text-amber-300 p-4">
      <p className="font-semibold">Paid period ended</p>
      <p className="text-sm mt-1">These businesses keep working until you set a new date or disable them.</p>
      <ul className="mt-2 space-y-1">
        {data.map((b) => (
          <li key={b.id} className="text-sm">
            <Link to={`/platform-admin/businesses/${b.id}`} className="underline font-medium">{b.name}</Link>
            {' '}· ended {format(parseISO(b.paidUntil), 'd MMM yyyy')}
          </li>
        ))}
      </ul>
    </div>
  )
}
