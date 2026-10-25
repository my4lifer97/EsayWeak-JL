import { t } from '../../lib/i18n'

// The customer's answer to the day-before reminder's "will you come? 1/2" (backend
// AttendanceQuestion): ✅ confirmed, or still waiting. Nothing when they weren't asked, or for an
// appointment that's no longer upcoming. A "no" cancels the appointment, so it shows as cancelled.
export default function AttendanceBadge({ lang, attendanceStatus, status }: {
  lang: string; attendanceStatus: string | null | undefined; status: string
}) {
  if (status !== 'CONFIRMED' || !attendanceStatus) return null
  const confirmed = attendanceStatus === 'CONFIRMED'
  return (
    <span className={`inline-block px-2 py-0.5 rounded-full text-xs font-medium ${
      confirmed ? 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400' : 'bg-cream text-muted border border-line'
    }`}>
      {confirmed ? `✅ ${t(lang, 'attendanceConfirmed')}` : `⏳ ${t(lang, 'attendanceAsked')}`}
    </span>
  )
}
