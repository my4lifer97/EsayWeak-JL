import { useState } from 'react'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t, itemName } from '../../lib/i18n'
import BackButton from '../../components/BackButton'
import InlineConfirm from '../../components/customer/InlineConfirm'
import { localToday } from '../../lib/localDate'
import { parseISO } from 'date-fns'
import { formatDay } from '../../lib/dateLocale'
import { useCustomerAuth } from '../../lib/customerAuth'

type AppointmentDetail = {
  id: string; date: string; startTime: string; endTime: string; status: string; notes: string | null; cancelToken: string
  item: { id: string; nameEn: string; nameAr: string; nameHe: string; isBookable: boolean }
  customer: { name: string; phone: string }
  business: { name: string; language: string }
}

type Slot = { start: string; end: string }
type SlotsResponse = { slots: Slot[]; isBlocked?: boolean; blockedReason?: string | null }

export default function AppointmentPage() {
  const { slug, id } = useParams<{ slug: string; id: string }>()
  const [searchParams] = useSearchParams()
  const token = searchParams.get('token') ?? ''
  const [loading, setLoading] = useState(false)
  const [showReschedule, setShowReschedule] = useState(false)
  const [rescheduleDate, setRescheduleDate] = useState('')
  const [rescheduleError, setRescheduleError] = useState('')
  const [rescheduling, setRescheduling] = useState(false)
  const [justRescheduled, setJustRescheduled] = useState(false)
  const [confirmingCancel, setConfirmingCancel] = useState(false)
  const [cancelError, setCancelError] = useState('')
  // Until the appointment (and its business's language) loads, or when it can't: the customer's own.
  const { language: customerLang } = useCustomerAuth()

  const { data: appt, isLoading, refetch } = useQuery<AppointmentDetail>({
    queryKey: ['appointment', id],
    queryFn: () => api.get(`/${slug}/appointments/${id}?token=${token}`).then((r) => r.data),
  })

  const { data: availability, isFetching: loadingSlots, isError: slotsFailed, refetch: refetchSlots } = useQuery<SlotsResponse>({
    queryKey: ['reschedule-slots', id, rescheduleDate],
    enabled: showReschedule && !!rescheduleDate,
    queryFn: () =>
      // Excluding this appointment, so it doesn't block its own neighbouring slots.
      api.get(`/${slug}/availability?date=${rescheduleDate}&itemId=${appt!.item.id}&excludeAppointmentId=${id}&token=${token}`).then((r) => r.data),
  })
  const slots = availability?.slots ?? []

  const fallbackDir = customerLang === 'AR' || customerLang === 'HE' ? 'rtl' : 'ltr'
  if (isLoading) return <div className="min-h-screen bg-cream flex items-center justify-center" dir={fallbackDir}><div className="text-muted">{t(customerLang, 'loading')}</div></div>
  // Opened from an old or mangled WhatsApp link, so give a way on rather than a dead end.
  if (!appt) return (
    <div className="min-h-screen bg-cream text-ink flex items-center justify-center px-4" dir={fallbackDir}>
      <div className="text-center space-y-4">
        <p className="text-muted">{t(customerLang, 'appointmentNotFound')}</p>
        <div className="flex flex-wrap justify-center gap-3">
          <Link to={`/${slug}`} className="bg-coral hover:bg-coral-dark text-white font-semibold px-5 py-2.5 rounded-xl transition-colors">{t(customerLang, 'viewBusinessPage')}</Link>
          <Link to="/" className="border border-line px-5 py-2.5 rounded-xl text-ink">{t(customerLang, 'goHome')}</Link>
        </div>
      </div>
    </div>
  )

  const lang = appt.business.language
  const isRTL = lang === 'AR' || lang === 'HE'
  const isCancelled = appt.status === 'CANCELLED'
  // The customer asked to cancel and the owner still has to approve it.
  const isPending = appt.status === 'CANCELLATION_PENDING'

  async function cancelAppointment() {
    setLoading(true); setCancelError('')
    try {
      await api.delete(`/${slug}/appointments/${id}?token=${token}`, { headers: { 'X-Lang': lang } })
      // Refetch rather than assume "cancelled": with owner approval on, it's only pending.
      await refetch()
      setConfirmingCancel(false)
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setCancelError(msg ?? t(lang, 'somethingWentWrong'))
    } finally {
      setLoading(false)
    }
  }

  function toggleReschedule() {
    setShowReschedule((v) => !v)
    setRescheduleDate('')
    setRescheduleError('')
  }

  async function confirmReschedule(startTime: string) {
    setRescheduling(true)
    setRescheduleError('')
    try {
      await api.patch(`/${slug}/appointments/${id}?token=${token}`, { date: rescheduleDate, startTime }, { headers: { 'X-Lang': lang } })
      setShowReschedule(false)
      setRescheduleDate('')
      await refetch()
      setJustRescheduled(true)
      setTimeout(() => setJustRescheduled(false), 4000)
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setRescheduleError(msg ?? t(lang, 'noTimes'))
    } finally {
      setRescheduling(false)
    }
  }

  const statusLabel = isCancelled
    ? t(lang, 'statusCancelled')
    : isPending
      ? t(lang, 'cancellationRequestedBadge')
    : appt.status === 'COMPLETED'
      ? t(lang, 'statusCompleted')
      : t(lang, 'statusConfirmed')

  const rows = [
    { label: t(lang, 'service'), value: itemName(appt.item, lang) },
    { label: t(lang, 'date'), value: formatDay(parseISO(appt.date.slice(0, 10)), lang, 'shortYear') },
    { label: t(lang, 'time'), value: `${appt.startTime} – ${appt.endTime}` },
    { label: t(lang, 'name'), value: appt.customer.name },
  ]

  return (
    <div className="min-h-screen bg-cream flex items-center justify-center p-4" dir={isRTL ? 'rtl' : 'ltr'}>
      <div className="w-full max-w-sm">
        <BackButton lang={lang} />
        <h1 className="text-2xl font-bold text-ink text-center mb-2 mt-3">{appt.business.name}</h1>
        <p className="text-muted text-center text-sm mb-8">{t(lang, 'yourAppointment')}</p>

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4 mb-6">
          {rows.map(({ label, value }) => (
            <div key={label} className="flex justify-between text-sm">
              <span className="text-muted">{label}</span>
              <span className="text-ink"><bdi>{value}</bdi></span>
            </div>
          ))}
          {appt.notes && (
            <div className="text-sm pt-2 border-t border-line">
              <span className="text-muted block mb-1">{t(lang, 'notesLabel')}</span>
              <span className="text-ink">{appt.notes}</span>
            </div>
          )}
          <div className="flex justify-between text-sm pt-2 border-t border-line">
            <span className="text-muted">{t(lang, 'status')}</span>
            <span className={isCancelled ? 'text-red-600 dark:text-red-400' : isPending ? 'text-amber-600 dark:text-amber-400' : appt.status === 'COMPLETED' ? 'text-green-600 dark:text-green-400' : 'text-blue-600 dark:text-blue-400'}>
              {statusLabel}
            </span>
          </div>
        </div>

        {!isCancelled && appt.status === 'CONFIRMED' && (
          <div className="flex gap-3">
            {appt.item.isBookable && (
              <button onClick={toggleReschedule} disabled={loading}
                className="flex-1 border border-line text-ink hover:bg-cream font-medium py-3 rounded-xl transition-colors disabled:opacity-50">
                {t(lang, 'editAppointment')}
              </button>
            )}
            <button onClick={() => { setConfirmingCancel(true); setCancelError('') }} disabled={loading}
              className="flex-1 bg-red-50 hover:bg-red-100 border border-red-200 text-red-600 dark:bg-red-950/40 dark:hover:bg-red-900/50 dark:border-red-800/50 dark:text-red-400 font-medium py-3 rounded-xl transition-colors disabled:opacity-50">
              {t(lang, 'cancelAppointment')}
            </button>
          </div>
        )}

        {confirmingCancel && !isCancelled && (
          <div className="mt-3">
            <InlineConfirm lang={lang} question={t(lang, 'cancelConfirm')} busy={loading}
              onYes={cancelAppointment} onNo={() => setConfirmingCancel(false)} />
          </div>
        )}
        {cancelError && <p className="mt-2 text-red-600 text-sm">{cancelError}</p>}

        {showReschedule && (
          <div className="mt-4 bg-surface border border-line rounded-2xl p-4 space-y-3">
            <p className="text-muted text-sm">{t(lang, 'selectNewTime')}</p>
            <input
              type="date" value={rescheduleDate}
              min={localToday()}
              onChange={(e) => { setRescheduleDate(e.target.value); setRescheduleError('') }}
              className="w-full bg-cream border border-line rounded-xl px-3 py-2 text-ink text-sm focus:outline-none focus:ring-2 focus:ring-coral"
            />
            {rescheduleDate && (
              loadingSlots ? (
                <div className="text-center text-muted text-sm py-3">{t(lang, 'loadingTimes')}</div>
              ) : slotsFailed ? (
                <div className="text-center text-muted text-sm py-3">
                  <p className="mb-2">{t(lang, 'timesLoadFailed')}</p>
                  <button type="button" onClick={() => refetchSlots()} className="text-coral-dark hover:text-coral font-medium">{t(lang, 'retry')}</button>
                </div>
              ) : slots.length === 0 ? (
                // Like the booking wizard: a day the owner closed says so, with their reason (QA run 3).
                <div className="text-center text-muted text-sm py-3">
                  <p>{availability?.isBlocked ? t(lang, 'closedByOwner') : t(lang, 'noTimes')}</p>
                  {availability?.isBlocked && availability.blockedReason && <p className="text-xs mt-1">{availability.blockedReason}</p>}
                </div>
              ) : (
                <div className="grid grid-cols-4 gap-2">
                  {slots.map((s) => (
                    <button
                      key={s.start}
                      disabled={rescheduling}
                      onClick={() => confirmReschedule(s.start)}
                      className="bg-cream hover:bg-coral hover:text-white border border-line text-ink text-sm py-2 rounded-lg transition-colors disabled:opacity-50">
                      {s.start}
                    </button>
                  ))}
                </div>
              )
            )}
            {rescheduleError && <p className="text-red-600 dark:text-red-400 text-xs">{rescheduleError}</p>}
          </div>
        )}

        {isCancelled && (
          <div className="text-center text-green-600 dark:text-green-400 text-sm">{t(lang, 'appointmentCancelledMsg')}</div>
        )}
        {justRescheduled && (
          <div className="text-center text-green-600 dark:text-green-400 text-sm mt-3">{t(lang, 'appointmentRescheduledMsg')}</div>
        )}
      </div>
    </div>
  )
}
