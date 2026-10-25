import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { apiError } from '../../lib/apiError'
import { itemName, t, type TKey } from '../../lib/i18n'
import { useConfirmDialog } from '../ConfirmDialog'

type QuickReply = { id: string; title: string; text: string }
type Service = { id: string; nameEn: string; nameAr: string; nameHe: string; isActive: boolean; isBookable: boolean; durationMinutes: number | null }

// What each "From the system" message needs picked first (ChatsController.GetSystemMessage).
const SYSTEM: { kind: string; key: TKey; service?: 'any' | 'bookable'; date?: boolean }[] = [
  { kind: 'services', key: 'readyServices' },
  { kind: 'service', key: 'readyService', service: 'any' },
  { kind: 'week', key: 'readyWeek' },
  { kind: 'day', key: 'readyDay', date: true },
  { kind: 'freeTimes', key: 'readyFreeTimes', service: 'bookable', date: true },
  { kind: 'address', key: 'readyAddress' },
  { kind: 'page', key: 'readyPage' },
  { kind: 'appointments', key: 'readyAppointments' },
]

// The Chats page's ⚡ Ready messages: texts built from the business's data in the customer's
// language, or the owner's own saved ones. Picking one puts it in the message box (onPick), where
// the owner can still edit it before sending.
export default function ReadyMessagesModal({ lang, phone, onPick, onClose }: {
  lang: string; phone: string; onPick: (text: string) => void; onClose: () => void
}) {
  const [tab, setTab] = useState<'system' | 'mine'>('system')

  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
      <div onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true"
        className="bg-surface rounded-2xl p-6 w-full max-w-lg border border-line max-h-[90vh] flex flex-col">
        <div className="flex items-start justify-between gap-3 mb-3">
          <h2 className="text-ink font-semibold text-lg">⚡ {t(lang, 'chatsReady')}</h2>
          <button onClick={onClose} aria-label={t(lang, 'close')}
            className="-m-2 w-11 h-11 flex items-center justify-center rounded-lg text-muted hover:text-ink hover:bg-cream text-2xl leading-none">✕</button>
        </div>
        <div className="flex gap-1 bg-cream rounded-lg p-1 mb-3 w-fit">
          {(['system', 'mine'] as const).map((k) => (
            <button key={k} onClick={() => setTab(k)}
              className={`px-3 py-1.5 rounded-md text-sm font-medium ${tab === k ? 'bg-coral text-white' : 'text-muted hover:text-ink'}`}>
              {t(lang, k === 'system' ? 'readySystemTab' : 'readyMineTab')}
            </button>
          ))}
        </div>
        {tab === 'system' && <p className="text-muted text-xs mb-3">{t(lang, 'readyHint')}</p>}
        <div className="overflow-y-auto -mx-1 px-1">
          {tab === 'system'
            ? <SystemMessages lang={lang} phone={phone} onPick={onPick} />
            : <MyMessages lang={lang} onPick={onPick} />}
        </div>
      </div>
    </div>
  )
}

function SystemMessages({ lang, phone, onPick }: { lang: string; phone: string; onPick: (text: string) => void }) {
  const [serviceId, setServiceId] = useState<Record<string, string>>({})
  const [date, setDate] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<{ kind: string; text: string } | null>(null)
  const { data: services = [] } = useQuery<Service[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/items').then((r) => r.data),
  })

  async function use(kind: string) {
    setBusy(kind); setError(null)
    try {
      const { data } = await api.get('/admin/chats/system-message', {
        params: { phone, kind, date: date[kind] || undefined, itemId: serviceId[kind] || undefined },
      })
      onPick(data.text)
    } catch (err) {
      setError({ kind, text: apiError(err) ?? t(lang, 'chatsSendError') })
    } finally {
      setBusy(null)
    }
  }

  const input = 'rounded-lg border border-line bg-cream px-2 py-1.5 text-sm text-ink'
  return (
    <ul className="space-y-2">
      {SYSTEM.map((s) => {
        const options = services.filter((x) => x.isActive && (s.service !== 'bookable' || (x.isBookable && x.durationMinutes)))
        const ready = (!s.service || !!serviceId[s.kind]) && (!s.date || !!date[s.kind])
        return (
          <li key={s.kind} className="rounded-xl border border-line p-3">
            <div className="flex flex-wrap items-center gap-2">
              <span className="text-sm font-medium text-ink flex-1 min-w-40">{t(lang, s.key)}</span>
              {s.service && (
                <select value={serviceId[s.kind] ?? ''} onChange={(e) => setServiceId((v) => ({ ...v, [s.kind]: e.target.value }))} className={input}>
                  <option value="">{t(lang, 'chatsPickService')}</option>
                  {options.map((x) => <option key={x.id} value={x.id}>{itemName(x, lang)}</option>)}
                </select>
              )}
              {s.date && (
                <input type="date" value={date[s.kind] ?? ''} onChange={(e) => setDate((v) => ({ ...v, [s.kind]: e.target.value }))} className={input} />
              )}
              <button onClick={() => use(s.kind)} disabled={!ready || busy !== null}
                className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-medium rounded-lg px-3 py-1.5">
                {t(lang, 'readyUse')}
              </button>
            </div>
            {error?.kind === s.kind && <p className="text-sm text-red-600 mt-2">{error.text}</p>}
          </li>
        )
      })}
    </ul>
  )
}

function MyMessages({ lang, onPick }: { lang: string; onPick: (text: string) => void }) {
  const queryClient = useQueryClient()
  const [ask, confirmDialog] = useConfirmDialog(lang)
  const [editing, setEditing] = useState<QuickReply | 'new' | null>(null)
  const [title, setTitle] = useState('')
  const [text, setText] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')
  const { data: replies = [], isLoading } = useQuery<QuickReply[]>({
    queryKey: ['quick-replies'],
    queryFn: () => api.get('/admin/chats/quick-replies').then((r) => r.data),
  })

  function edit(reply: QuickReply | 'new') {
    setEditing(reply)
    setTitle(reply === 'new' ? '' : reply.title)
    setText(reply === 'new' ? '' : reply.text)
    setError('')
  }

  async function save() {
    setSaving(true); setError('')
    try {
      if (editing === 'new') await api.post('/admin/chats/quick-replies', { title, text })
      else if (editing) await api.put(`/admin/chats/quick-replies/${editing.id}`, { title, text })
      await queryClient.invalidateQueries({ queryKey: ['quick-replies'] })
      setEditing(null)
    } catch (err) {
      setError(apiError(err) ?? t(lang, 'failedToSave'))
    } finally {
      setSaving(false)
    }
  }

  async function remove(reply: QuickReply) {
    if (!(await ask(t(lang, 'readyDeleteConfirm'), { yesLabel: t(lang, 'confirmDeleteYes'), noLabel: t(lang, 'chatsCancel') }))) return
    await api.delete(`/admin/chats/quick-replies/${reply.id}`)
    queryClient.invalidateQueries({ queryKey: ['quick-replies'] })
  }

  const field = 'w-full rounded-lg border border-line bg-cream px-3 py-2 text-sm text-ink focus:outline-none focus:ring-2 focus:ring-coral'
  if (editing)
    return (
      <div className="space-y-2">
        <input value={title} onChange={(e) => setTitle(e.target.value)} maxLength={100} placeholder={t(lang, 'readyName')} className={field} />
        <textarea value={text} onChange={(e) => setText(e.target.value)} maxLength={4000} rows={5} placeholder={t(lang, 'readyText')} className={`${field} resize-none`} dir="auto" />
        {error && <p className="text-sm text-red-600">{error}</p>}
        <div className="flex gap-2">
          <button onClick={save} disabled={saving || !title.trim() || !text.trim()}
            className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-semibold rounded-lg px-4 py-2">
            {saving ? t(lang, 'saving') : t(lang, 'readySave')}
          </button>
          <button onClick={() => setEditing(null)} className="text-sm text-muted hover:text-ink px-3">{t(lang, 'chatsCancel')}</button>
        </div>
      </div>
    )

  return (
    <div className="space-y-2">
      {confirmDialog}
      <button onClick={() => edit('new')} className="w-full border border-dashed border-coral text-coral-dark hover:bg-coral-tint text-sm font-medium rounded-xl py-2">
        {t(lang, 'readyNew')}
      </button>
      {isLoading ? <p className="text-muted text-sm">{t(lang, 'loading')}</p>
        : replies.length === 0 ? <p className="text-muted text-sm text-center py-4">{t(lang, 'readyEmpty')}</p>
        : replies.map((r) => (
          <div key={r.id} className="rounded-xl border border-line p-3">
            <div className="flex items-center gap-2">
              <span className="text-sm font-medium text-ink flex-1 truncate">{r.title}</span>
              <button onClick={() => edit(r)} className="text-xs text-muted hover:text-ink">{t(lang, 'readyEdit')}</button>
              <button onClick={() => remove(r)} className="text-xs text-red-600 hover:underline">{t(lang, 'readyDelete')}</button>
              <button onClick={() => onPick(r.text)} className="bg-coral hover:bg-coral-dark text-white text-sm font-medium rounded-lg px-3 py-1.5">
                {t(lang, 'readyUse')}
              </button>
            </div>
            <p className="text-muted text-xs mt-1 line-clamp-2 whitespace-pre-wrap" dir="auto">{r.text}</p>
          </div>
        ))}
    </div>
  )
}
