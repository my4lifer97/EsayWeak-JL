import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'
import CustomerPicker, { type CustomerSelection } from './CustomerPicker'

export default function CancelOptionsModal({
  lang, appointmentId, waitlistEnabled, pendingCancellation = false, onClose, onDone,
}: {
  lang: string
  appointmentId: string
  waitlistEnabled: boolean
  // The customer asked to cancel and the owner is deciding -- adds "keep the appointment".
  pendingCancellation?: boolean
  onClose: () => void
  onDone: () => void
}) {
  const [mode, setMode] = useState<'options' | 'replace'>('options')
  const [replaceSelection, setReplaceSelection] = useState<CustomerSelection | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  // Cancel, keep and replace each send the customer a WhatsApp message by default.
  const [notifyCustomer, setNotifyCustomer] = useState(true)

  // Every action shows the server's reason on failure instead of silently doing nothing.
  async function run(action: () => Promise<unknown>) {
    setLoading(true); setError('')
    try {
      await action()
      onDone()
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { error?: string } } })?.response?.data?.error
      setError(msg ?? t(lang, 'somethingWentWrong'))
    } finally { setLoading(false) }
  }

  const { data: waitlistEntries = [] } = useQuery<{ id: string; name: string; familyName: string; phone: string }[]>({
    queryKey: ['appointment-waitlist', appointmentId],
    queryFn: () => api.get(`/admin/appointments/${appointmentId}/waitlist`).then((r) => r.data),
  })

  // Same query key as the Settings page; the server only messages customers when a WhatsApp
  // number is linked, so the checkbox is shown only then.
  const { data: settings } = useQuery<{ whatsAppNumber: string | null }>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  function cancel(notifyWaitlist: boolean) {
    return run(() => api.patch(`/admin/appointments/${appointmentId}`, { status: 'CANCELLED', notifyWaitlist, notifyCustomer }))
  }

  function keep() {
    return run(() => api.post(`/admin/appointments/${appointmentId}/keep?notifyCustomer=${notifyCustomer}`))
  }

  async function replaceCustomer() {
    if (!replaceSelection) return
    const target = 'waitlistEntryId' in replaceSelection ? { waitlistEntryId: replaceSelection.waitlistEntryId }
      : 'customerId' in replaceSelection ? { customerId: replaceSelection.customerId }
      : { customerName: replaceSelection.customerName, customerFamilyName: replaceSelection.customerFamilyName, customerPhone: replaceSelection.customerPhone }
    return run(() => api.patch(`/admin/appointments/${appointmentId}/customer`, { ...target, notifyCustomer }))
  }

  const notifyCustomerToggle = settings?.whatsAppNumber ? (
    <label className="flex items-center gap-2 text-sm text-ink mb-3 cursor-pointer">
      <input type="checkbox" checked={notifyCustomer} onChange={(e) => setNotifyCustomer(e.target.checked)}
        className="w-4 h-4 accent-coral" />
      {t(lang, 'notifyCustomerWhatsApp')}
    </label>
  ) : null

  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 w-full max-w-sm border border-line">
        <div className="flex justify-between items-start mb-4">
          <h2 className="text-ink font-semibold text-lg">{t(lang, 'cancelOptionsTitle')}</h2>
          <button onClick={onClose} aria-label={t(lang, 'close')}
            className="text-muted hover:text-ink w-11 h-11 -m-2 flex items-center justify-center rounded-lg hover:bg-cream text-2xl leading-none transition-colors">✕</button>
        </div>

        {error && <p className="text-red-600 dark:text-red-400 text-sm mb-3">{error}</p>}
        {notifyCustomerToggle}

        {mode === 'options' ? (
          <div className="space-y-4">
            <div className="space-y-2">
              {pendingCancellation && (
                <button type="button" disabled={loading} onClick={keep}
                  className="w-full text-start bg-teal-tint hover:bg-teal-tint/70 disabled:opacity-50 text-ink rounded-xl px-4 py-3 transition-colors">
                  <div className="text-sm font-semibold">{t(lang, 'keepAppointment')}</div>
                  <div className="text-xs text-muted mt-0.5">{t(lang, 'keepAppointmentHint')}</div>
                </button>
              )}
              {waitlistEnabled && (
                <button type="button" disabled={loading} onClick={() => cancel(true)}
                  className="w-full text-start bg-coral hover:bg-coral-dark disabled:opacity-50 text-white rounded-xl px-4 py-3 transition-colors">
                  <div className="text-sm font-semibold">{t(lang, 'offerToWaitlist')}</div>
                  <div className="text-xs text-coral-tint mt-0.5">{t(lang, 'offerToWaitlistHint')}</div>
                </button>
              )}
              <button type="button" disabled={loading} onClick={() => cancel(false)}
                className="w-full text-start bg-teal-tint hover:bg-teal-tint/70 disabled:opacity-50 text-ink rounded-xl px-4 py-3 transition-colors">
                <div className="text-sm font-semibold">{t(lang, 'cancelSilently')}</div>
                {waitlistEnabled && <div className="text-xs text-muted mt-0.5">{t(lang, 'cancelSilentlyHint')}</div>}
              </button>
              <button type="button" disabled={loading} onClick={() => setMode('replace')}
                className="w-full text-start bg-teal-tint hover:bg-teal-tint/70 disabled:opacity-50 text-ink rounded-xl px-4 py-3 transition-colors">
                <div className="text-sm font-semibold">{t(lang, 'replaceCustomer')}</div>
                <div className="text-xs text-muted mt-0.5">{t(lang, 'replaceCustomerHint')}</div>
              </button>
            </div>

            <div>
              <div className="text-xs font-semibold text-muted uppercase tracking-wide mb-1.5">
                {t(lang, 'currentWaitlistTitle')} {waitlistEntries.length > 0 && `(${waitlistEntries.length})`}
              </div>
              {waitlistEntries.length === 0 ? (
                <div className="text-sm text-muted">{t(lang, 'noOneOnWaitlist')}</div>
              ) : (
                <div className="bg-cream border border-line rounded-lg divide-y divide-line max-h-32 overflow-y-auto">
                  {waitlistEntries.map((w) => (
                    <div key={w.id} className="px-3 py-1.5 text-sm text-ink">
                      {w.name} {w.familyName} · <bdi dir="ltr">{w.phone}</bdi>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="space-y-4">
            <CustomerPicker lang={lang} value={replaceSelection} onChange={setReplaceSelection} waitlistEntries={waitlistEntries} />
            <div className="flex gap-2">
              <button type="button" onClick={() => { setMode('options'); setError('') }}
                className="flex-1 bg-teal-tint hover:bg-teal-tint/70 text-ink text-sm font-medium py-2.5 rounded-lg transition-colors">
                {t(lang, 'back')}
              </button>
              <button type="button" disabled={!replaceSelection || loading} onClick={replaceCustomer}
                className="flex-1 bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-medium py-2.5 rounded-lg transition-colors">
                {t(lang, 'confirmReplaceCustomer')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
