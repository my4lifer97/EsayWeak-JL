import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'

type ChatbotSettings = {
  chatbotEnabled: boolean
  chatbotWelcomeMessage: string | null
  chatbotConfirmationMessage: string | null
  chatbotFinalMessage: string | null
  chatbotInquiryEnabled: boolean
  inquiryNotifyViaWhatsApp: boolean
  inquiryWhatsAppNumber: string | null
  inquiryNotifyViaEmail: boolean
  inquiryEmail: string | null
}

export default function ChatbotSettingsPage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()
  const { data: business } = useQuery<ChatbotSettings>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  const [form, setForm] = useState({
    chatbotWelcomeMessage: '', chatbotConfirmationMessage: '', chatbotFinalMessage: '',
    inquiryWhatsAppNumber: '', inquiryEmail: '',
  })
  const [chatbotEnabled, setChatbotEnabled] = useState(true)
  const [chatbotInquiryEnabled, setChatbotInquiryEnabled] = useState(false)
  const [inquiryNotifyViaWhatsApp, setInquiryNotifyViaWhatsApp] = useState(false)
  const [inquiryNotifyViaEmail, setInquiryNotifyViaEmail] = useState(false)
  const [initialized, setInitialized] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState('')

  if (business && !initialized) {
    setForm({
      chatbotWelcomeMessage: business.chatbotWelcomeMessage ?? '',
      chatbotConfirmationMessage: business.chatbotConfirmationMessage ?? '',
      chatbotFinalMessage: business.chatbotFinalMessage ?? '',
      inquiryWhatsAppNumber: business.inquiryWhatsAppNumber ?? '',
      inquiryEmail: business.inquiryEmail ?? '',
    })
    setChatbotEnabled(business.chatbotEnabled)
    setChatbotInquiryEnabled(business.chatbotInquiryEnabled)
    setInquiryNotifyViaWhatsApp(business.inquiryNotifyViaWhatsApp)
    setInquiryNotifyViaEmail(business.inquiryNotifyViaEmail)
    setInitialized(true)
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault(); setSaving(true); setError('')
    const payload = {
      chatbotEnabled,
      chatbotWelcomeMessage: form.chatbotWelcomeMessage || null,
      chatbotConfirmationMessage: form.chatbotConfirmationMessage || null,
      chatbotFinalMessage: form.chatbotFinalMessage || null,
      chatbotInquiryEnabled,
      inquiryNotifyViaWhatsApp,
      inquiryWhatsAppNumber: form.inquiryWhatsAppNumber || null,
      inquiryNotifyViaEmail,
      inquiryEmail: form.inquiryEmail || null,
    }
    try {
      await api.patch('/admin/settings', payload)
      setSaved(true); setTimeout(() => setSaved(false), 2000)
      queryClient.invalidateQueries({ queryKey: ['settings'] })
    } catch (err) {
      const msg = (err as { response?: { data?: { error?: string } } }).response?.data?.error
      setError(msg || 'Failed to save')
    } finally { setSaving(false) }
  }

  if (!business) return <div className="text-muted">{t(lang, 'loading')}</div>

  return (
    <div>
      <h1 className="text-2xl font-bold text-ink mb-6">{t(lang, 'chatbotSettings')}</h1>
      <form onSubmit={handleSubmit} className="space-y-6 max-w-2xl">
        {error && <div className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-4 py-3">{error}</div>}

        <div className="bg-surface border border-line rounded-2xl p-6 space-y-4">
          <p className="text-muted text-sm">{t(lang, 'chatbotSettingsHint')}</p>

          <label className="flex items-start gap-3 cursor-pointer">
            <input type="checkbox" checked={chatbotEnabled}
              onChange={(e) => setChatbotEnabled(e.target.checked)}
              className="mt-1 w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
            <span>
              <span className="block text-sm font-medium text-ink">{t(lang, 'chatbotEnabledLabel')}</span>
              <span className="block text-muted text-sm mt-0.5">{t(lang, 'chatbotEnabledHint')}</span>
            </span>
          </label>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'chatbotWelcomeMessage')}</label>
            <p className="text-muted text-xs mb-1.5">{t(lang, 'chatbotWelcomeMessageHint')}</p>
            <textarea value={form.chatbotWelcomeMessage} rows={2}
              onChange={(e) => setForm((f) => ({ ...f, chatbotWelcomeMessage: e.target.value }))}
              placeholder={t(lang, 'chatbotDefaultTextPlaceholder')}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
          </div>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'chatbotConfirmationMessage')}</label>
            <p className="text-muted text-xs mb-1.5">{t(lang, 'chatbotConfirmationMessageHint')}</p>
            <textarea value={form.chatbotConfirmationMessage} rows={2}
              onChange={(e) => setForm((f) => ({ ...f, chatbotConfirmationMessage: e.target.value }))}
              placeholder={t(lang, 'chatbotDefaultTextPlaceholder')}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
          </div>

          <div>
            <label className="block text-sm font-medium text-ink mb-1.5">{t(lang, 'chatbotFinalMessage')}</label>
            <p className="text-muted text-xs mb-1.5">{t(lang, 'chatbotFinalMessageHint')}</p>
            <textarea value={form.chatbotFinalMessage} rows={2}
              onChange={(e) => setForm((f) => ({ ...f, chatbotFinalMessage: e.target.value }))}
              placeholder={t(lang, 'chatbotDefaultTextPlaceholder')}
              className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral resize-none" />
          </div>

          <div className="border-t border-line pt-4">
            <label className="flex items-start gap-3 cursor-pointer">
              <input type="checkbox" checked={chatbotInquiryEnabled}
                onChange={(e) => setChatbotInquiryEnabled(e.target.checked)}
                className="mt-1 w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
              <span>
                <span className="block text-sm font-medium text-ink">{t(lang, 'chatbotInquiryEnabledLabel')}</span>
                <span className="block text-muted text-sm mt-0.5">{t(lang, 'chatbotInquiryEnabledHint')}</span>
              </span>
            </label>

            {chatbotInquiryEnabled && (
              <div className="mt-4 ms-7 space-y-3">
                <h3 className="text-sm font-semibold text-ink">{t(lang, 'inquiryNotifications')}</h3>

                <label className="flex items-center gap-3 cursor-pointer">
                  <input type="checkbox" checked={inquiryNotifyViaWhatsApp}
                    onChange={(e) => setInquiryNotifyViaWhatsApp(e.target.checked)}
                    className="w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
                  <span className="text-sm text-ink">{t(lang, 'inquiryNotifyViaWhatsApp')}</span>
                </label>
                {inquiryNotifyViaWhatsApp && (
                  <input value={form.inquiryWhatsAppNumber} type="tel"
                    onChange={(e) => setForm((f) => ({ ...f, inquiryWhatsAppNumber: e.target.value }))}
                    placeholder={t(lang, 'inquiryWhatsAppNumberPlaceholder')}
                    className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral" />
                )}

                <label className="flex items-center gap-3 cursor-pointer">
                  <input type="checkbox" checked={inquiryNotifyViaEmail}
                    onChange={(e) => setInquiryNotifyViaEmail(e.target.checked)}
                    className="w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
                  <span className="text-sm text-ink">{t(lang, 'inquiryNotifyViaEmail')}</span>
                </label>
                {inquiryNotifyViaEmail && (
                  <input value={form.inquiryEmail} type="email"
                    onChange={(e) => setForm((f) => ({ ...f, inquiryEmail: e.target.value }))}
                    placeholder={t(lang, 'inquiryEmailPlaceholder')}
                    className="w-full bg-cream border border-line rounded-lg px-3 py-2.5 text-ink placeholder-muted focus:outline-none focus:ring-2 focus:ring-coral" />
                )}

                <p className="text-muted text-xs mb-3">{t(lang, 'inquiryInboxHint')}</p>
                <ChatbotInquiriesInbox lang={lang} />
              </div>
            )}
          </div>
        </div>

        <button type="submit" disabled={saving}
          className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold px-8 py-2.5 rounded-lg transition-colors">
          {saved ? t(lang, 'saved') : saving ? t(lang, 'saving') : t(lang, 'saveChanges')}
        </button>
      </form>
    </div>
  )
}

type ChatbotInquiry = {
  id: string; customerPhone: string; customerName: string | null; message: string
  isRead: boolean; createdAt: string
}

function ChatbotInquiriesInbox({ lang }: { lang: string }) {
  const queryClient = useQueryClient()
  const [unreadOnly, setUnreadOnly] = useState(false)
  const [busyId, setBusyId] = useState<string | null>(null)

  const { data: inquiries = [], isLoading } = useQuery<ChatbotInquiry[]>({
    queryKey: ['admin-chatbot-inquiries', unreadOnly],
    queryFn: () => api.get(`/admin/chatbot-inquiries${unreadOnly ? '?unreadOnly=true' : ''}`).then((r) => r.data),
  })

  async function markRead(id: string) {
    setBusyId(id)
    try {
      await api.post(`/admin/chatbot-inquiries/${id}/read`)
      queryClient.invalidateQueries({ queryKey: ['admin-chatbot-inquiries'] })
    } finally { setBusyId(null) }
  }

  return (
    <div className="border-t border-line pt-3">
      <div className="flex items-center justify-between mb-3">
        <h3 className="text-sm font-semibold text-ink">{t(lang, 'inquiries')}</h3>
        <label className="flex items-center gap-2 text-xs text-ink cursor-pointer">
          <input type="checkbox" checked={unreadOnly} onChange={(e) => setUnreadOnly(e.target.checked)}
            className="w-3.5 h-3.5 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
          {t(lang, 'unreadOnly')}
        </label>
      </div>

      {isLoading ? (
        <p className="text-muted text-sm">{t(lang, 'loading')}</p>
      ) : inquiries.length === 0 ? (
        <p className="text-muted text-sm">{t(lang, 'noInquiries')}</p>
      ) : (
        <div className="space-y-2 max-h-96 overflow-y-auto">
          {inquiries.map((i) => (
            <div key={i.id} className={`bg-cream border rounded-xl p-3 ${i.isRead ? 'border-line opacity-70' : 'border-coral/40'}`}>
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2 min-w-0">
                  <span className="text-ink text-sm font-medium truncate">{i.customerName || i.customerPhone}</span>
                  {i.customerName && <span className="text-muted text-xs shrink-0">{i.customerPhone}</span>}
                  {!i.isRead && (
                    <span className="text-xs px-2 py-0.5 rounded-full bg-coral/15 text-coral-dark shrink-0">{t(lang, 'unread')}</span>
                  )}
                </div>
                <span className="text-muted text-xs shrink-0">{format(parseISO(i.createdAt), 'MMM d · HH:mm')}</span>
              </div>
              <p className="text-ink text-sm mt-1.5 whitespace-pre-wrap">{i.message}</p>
              {!i.isRead && (
                <button type="button" onClick={() => markRead(i.id)} disabled={busyId === i.id}
                  className="mt-2 border border-line text-ink hover:bg-surface text-xs font-medium py-1 px-2.5 rounded-lg transition-colors disabled:opacity-50">
                  {t(lang, 'markAsRead')}
                </button>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
