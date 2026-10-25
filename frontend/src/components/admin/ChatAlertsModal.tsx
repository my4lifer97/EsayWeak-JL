import { useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { apiError } from '../../lib/apiError'
import { t } from '../../lib/i18n'

export type ChatAlertSettings = {
  inquiryNotifyViaWhatsApp: boolean
  inquiryWhatsAppNumber: string | null
  inquiryNotifyViaEmail: boolean
  inquiryEmail: string | null
}

// The Chats page's ⚙️: how the owner hears that a customer asked to talk to them (#2) -- a
// WhatsApp message to their own number and/or an email, once per chat. Saved through the same
// partial PATCH /admin/settings as the other settings pages (moved here from WhatsApp Chatbot
// settings, 2026-10-25).
export default function ChatAlertsModal({ lang, settings, onClose }: {
  lang: string; settings: ChatAlertSettings; onClose: () => void
}) {
  const queryClient = useQueryClient()
  const [viaWhatsApp, setViaWhatsApp] = useState(settings.inquiryNotifyViaWhatsApp)
  const [whatsAppNumber, setWhatsAppNumber] = useState(settings.inquiryWhatsAppNumber ?? '')
  const [viaEmail, setViaEmail] = useState(settings.inquiryNotifyViaEmail)
  const [email, setEmail] = useState(settings.inquiryEmail ?? '')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  async function save() {
    setSaving(true)
    setError('')
    try {
      await api.patch('/admin/settings', {
        inquiryNotifyViaWhatsApp: viaWhatsApp,
        inquiryWhatsAppNumber: whatsAppNumber.trim() || null,
        inquiryNotifyViaEmail: viaEmail,
        inquiryEmail: email.trim() || null,
      })
      await queryClient.invalidateQueries({ queryKey: ['settings'] })
      onClose()
    } catch (err) {
      setError(apiError(err) ?? t(lang, 'failedToSave'))
    } finally {
      setSaving(false)
    }
  }

  const input = 'w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral'
  const checkbox = 'w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface'

  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true"
        className="bg-surface rounded-2xl p-6 w-full max-w-md border border-line max-h-[90vh] overflow-y-auto">
        <div className="flex items-start justify-between gap-3 mb-4">
          <h2 className="text-ink font-semibold text-lg">{t(lang, 'inquiryNotifications')}</h2>
          <button onClick={onClose} aria-label={t(lang, 'close')}
            className="-m-2 w-11 h-11 flex items-center justify-center rounded-lg text-muted hover:text-ink hover:bg-cream text-2xl leading-none">✕</button>
        </div>

        <div className="space-y-3">
          <p className="text-muted text-sm">{t(lang, 'chatsAlertsHint')}</p>
          <label className="flex items-center gap-3 cursor-pointer">
            <input type="checkbox" checked={viaWhatsApp} onChange={(e) => setViaWhatsApp(e.target.checked)} className={checkbox} />
            <span className="text-sm text-ink">{t(lang, 'inquiryNotifyViaWhatsApp')}</span>
          </label>
          {viaWhatsApp && (
            <input value={whatsAppNumber} type="tel" dir="ltr" onChange={(e) => setWhatsAppNumber(e.target.value)}
              placeholder={t(lang, 'inquiryWhatsAppNumberPlaceholder')} className={input} />
          )}

          <label className="flex items-center gap-3 cursor-pointer">
            <input type="checkbox" checked={viaEmail} onChange={(e) => setViaEmail(e.target.checked)} className={checkbox} />
            <span className="text-sm text-ink">{t(lang, 'inquiryNotifyViaEmail')}</span>
          </label>
          {viaEmail && (
            <input value={email} type="email" dir="ltr" onChange={(e) => setEmail(e.target.value)}
              placeholder={t(lang, 'inquiryEmailPlaceholder')} className={input} />
          )}

          {(viaWhatsApp || viaEmail) && <p className="text-muted text-xs">{t(lang, 'inquiryContactFallback')}</p>}
          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>

        <div className="flex gap-2 mt-6">
          <button onClick={save} disabled={saving}
            className="flex-1 bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold py-2.5 rounded-lg transition-colors">
            {saving ? t(lang, 'saving') : t(lang, 'saveChanges')}
          </button>
          <button onClick={onClose} className="px-4 py-2.5 rounded-lg text-muted hover:text-ink hover:bg-cream">{t(lang, 'chatsCancel')}</button>
        </div>
      </div>
    </div>
  )
}
