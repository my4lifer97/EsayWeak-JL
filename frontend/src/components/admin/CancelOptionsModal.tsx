import { useState } from 'react'
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
        'customerId' in replaceSelection
          ? { customerId: replaceSelection.customerId }
          : { customerName: replaceSelection.customerName, customerPhone: replaceSelection.customerPhone })
      onDone()
    } finally { setLoading(false) }
  }

  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div className="bg-gray-900 rounded-2xl p-6 w-full max-w-sm border border-gray-800">
        <div className="flex justify-between items-start mb-4">
          <h2 className="text-white font-semibold text-lg">{t(lang, 'cancelOptionsTitle')}</h2>
          <button onClick={onClose} className="text-gray-500 hover:text-white text-xl">✕</button>
        </div>

        {mode === 'options' ? (
          <div className="space-y-2">
            {waitlistEnabled && (
              <button type="button" disabled={loading} onClick={() => cancel(true)}
                className="w-full text-start bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white rounded-xl px-4 py-3 transition-colors">
                <div className="text-sm font-semibold">{t(lang, 'offerToWaitlist')}</div>
                <div className="text-xs text-blue-100 mt-0.5">{t(lang, 'offerToWaitlistHint')}</div>
              </button>
            )}
            <button type="button" disabled={loading} onClick={() => cancel(false)}
              className="w-full text-start bg-gray-800 hover:bg-gray-700 disabled:opacity-50 text-gray-200 rounded-xl px-4 py-3 transition-colors">
              <div className="text-sm font-semibold">{t(lang, 'cancelSilently')}</div>
            </button>
            <button type="button" disabled={loading} onClick={() => setMode('replace')}
              className="w-full text-start bg-gray-800 hover:bg-gray-700 disabled:opacity-50 text-gray-200 rounded-xl px-4 py-3 transition-colors">
              <div className="text-sm font-semibold">{t(lang, 'replaceCustomer')}</div>
              <div className="text-xs text-gray-400 mt-0.5">{t(lang, 'replaceCustomerHint')}</div>
            </button>
          </div>
        ) : (
          <div className="space-y-4">
            <CustomerPicker lang={lang} value={replaceSelection} onChange={setReplaceSelection} />
            <div className="flex gap-2">
              <button type="button" onClick={() => setMode('options')}
                className="flex-1 bg-gray-800 hover:bg-gray-700 text-gray-200 text-sm font-medium py-2.5 rounded-lg transition-colors">
                {t(lang, 'back')}
              </button>
              <button type="button" disabled={!replaceSelection || loading} onClick={replaceCustomer}
                className="flex-1 bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white text-sm font-medium py-2.5 rounded-lg transition-colors">
                {t(lang, 'confirmReplaceCustomer')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
