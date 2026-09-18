import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { t } from '../../lib/i18n'
import CustomerPicker, { type CustomerSelection } from './CustomerPicker'

export default function CancelOptionsModal({
  lang, appointmentId, waitlistEnabled, onClose, onDone,
}: {
  lang: string
  appointmentId: string
  waitlistEnabled: boolean
  onClose: () => void
  onDone: () => void
}) {
  const [mode, setMode] = useState<'options' | 'replace'>('options')
  const [replaceSelection, setReplaceSelection] = useState<CustomerSelection | null>(null)
  const [loading, setLoading] = useState(false)

  const { data: waitlistEntries = [] } = useQuery<{ id: string; name: string; familyName: string; phone: string }[]>({
    queryKey: ['appointment-waitlist', appointmentId],
    queryFn: () => api.get(`/admin/appointments/${appointmentId}/waitlist`).then((r) => r.data),
  })

  async function cancel(notifyWaitlist: boolean) {
    setLoading(true)
    try {
      await api.patch(`/admin/appointments/${appointmentId}`, { status: 'CANCELLED', notifyWaitlist })
      onDone()
    } finally { setLoading(false) }
  }

  async function replaceCustomer() {
    if (!replaceSelection) return
    setLoading(true)
    try {
      await api.patch(`/admin/appointments/${appointmentId}/customer`,
        'waitlistEntryId' in replaceSelection ? { waitlistEntryId: replaceSelection.waitlistEntryId }
          : 'customerId' in replaceSelection ? { customerId: replaceSelection.customerId }
          : { customerName: replaceSelection.customerName, customerFamilyName: replaceSelection.customerFamilyName, customerPhone: replaceSelection.customerPhone })
      onDone()
    } finally { setLoading(false) }
  }

  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 w-full max-w-sm border border-line">
        <div className="flex justify-between items-start mb-4">
          <h2 className="text-ink font-semibold text-lg">{t(lang, 'cancelOptionsTitle')}</h2>
          <button onClick={onClose} aria-label="Close"
            className="text-muted hover:text-ink w-11 h-11 -m-2 flex items-center justify-center rounded-lg hover:bg-cream text-2xl leading-none transition-colors">✕</button>
        </div>

        {mode === 'options' ? (
          <div className="space-y-4">
            <div className="space-y-2">
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
                      {w.name} {w.familyName} · {w.phone}
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
              <button type="button" onClick={() => setMode('options')}
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
