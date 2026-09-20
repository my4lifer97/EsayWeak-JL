import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, parseISO } from 'date-fns'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'

type ChatbotInquiry = {
  id: string; customerPhone: string; customerName: string | null; message: string
  isRead: boolean; createdAt: string
}

// The always-on "in-app" side of chatbot inquiry notifications (see WhatsAppController's
// $1/$2 Inquiry mode) -- every inquiry lands here regardless of whether WhatsApp/email
// notifications are also on (Settings > Chatbot Settings > Enable Customer Inquiries).
export default function InquiriesPage() {
  const queryClient = useQueryClient()
  const { language: lang } = useAuth()
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
    <div>
      <div className="flex items-center justify-between mb-6">
        <h1 className="text-2xl font-bold text-ink">{t(lang, 'inquiries')}</h1>
        <label className="flex items-center gap-2 text-sm text-ink cursor-pointer">
          <input type="checkbox" checked={unreadOnly} onChange={(e) => setUnreadOnly(e.target.checked)}
            className="w-4 h-4 rounded border-line bg-cream text-coral focus:ring-coral focus:ring-offset-surface" />
          {t(lang, 'unreadOnly')}
        </label>
      </div>

      {isLoading ? (
        <div className="text-muted py-12 text-center">{t(lang, 'loading')}</div>
      ) : inquiries.length === 0 ? (
        <div className="text-muted py-12 text-center">{t(lang, 'noInquiries')}</div>
      ) : (
        <div className="space-y-3">
          {inquiries.map((i) => (
            <div key={i.id} className={`bg-surface border rounded-2xl p-4 ${i.isRead ? 'border-line opacity-70' : 'border-coral/40'}`}>
              <div className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <span className="text-ink text-sm font-medium">{i.customerName || i.customerPhone}</span>
                  {i.customerName && <span className="text-muted text-xs">{i.customerPhone}</span>}
                  {!i.isRead && (
                    <span className="text-xs px-2 py-0.5 rounded-full bg-coral/15 text-coral-dark">{t(lang, 'unread')}</span>
                  )}
                </div>
                <span className="text-muted text-xs">{format(parseISO(i.createdAt), 'MMM d, yyyy · HH:mm')}</span>
              </div>
              <p className="text-ink text-sm mt-2 whitespace-pre-wrap">{i.message}</p>
              {!i.isRead && (
                <button onClick={() => markRead(i.id)} disabled={busyId === i.id}
                  className="mt-3 border border-line text-ink hover:bg-cream text-sm font-medium py-1.5 px-3 rounded-xl transition-colors disabled:opacity-50">
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
