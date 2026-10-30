import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, isToday, parseISO } from 'date-fns'
import { dateLocale } from '../../lib/dateLocale'
import { api } from '../../lib/api'
import { apiError } from '../../lib/apiError'
import { useAuth } from '../../lib/auth'
import { itemName, t } from '../../lib/i18n'
import { useIsTouchPrimary } from '../../lib/useIsTouchPrimary'
import UnreadBadge, { CHATS_UNREAD_KEY } from '../../components/admin/UnreadBadge'
import ChatAlertsModal, { type ChatAlertSettings } from '../../components/admin/ChatAlertsModal'
import NewAppointmentModal from '../../components/admin/NewAppointmentModal'
import ReadyMessagesModal from '../../components/admin/ReadyMessagesModal'
import type { CustomerSelection } from '../../components/admin/CustomerPicker'

type Sender = 'Customer' | 'Bot' | 'Owner' | 'OwnerPhone'
type ChatSummary = { phone: string; name: string | null; lastText: string; lastSender: Sender; lastAt: string; unread: number; isOpen: boolean }
type ChatAttachment = { id: string; fileName: string; contentType: string }
type ChatMessage = { id: string; sender: Sender; text: string; createdAt: string; attachment: ChatAttachment | null }

// Same list as the server's ChatFiles (10MB, checked there too).
const ACCEPTED_FILES = '.jpg,.jpeg,.png,.webp,.pdf,.doc,.docx,.xls,.xlsx'
const MAX_FILE_BYTES = 10 * 1024 * 1024
type Chat = { phone: string; name: string | null; isOpen: boolean; customerId: string | null; messages: ChatMessage[] }
const MAX_TEXT = 4000

// The typed text and chosen file of each chat while the page is open: switching chats remounts
// the conversation, which used to lose them (QA run 5).
const drafts = new Map<string, { text: string; file: File | null }>()
type Service = { id: string; nameEn: string; nameAr: string; nameHe: string; isBookable: boolean; isActive: boolean }

// Customers who chose to talk to the owner in the WhatsApp chatbot (#2), answered from here
// instead of the phone (ChatsController): reply, send a booking link, finish the chat.
// Polls, since messages arrive from WhatsApp.
export default function ChatsPage() {
  const { language: lang } = useAuth()
  const isTouch = useIsTouchPrimary()
  const queryClient = useQueryClient()
  const [params, setParams] = useSearchParams()
  const selected = params.get('phone')

  const { data: chats = [], isLoading } = useQuery<ChatSummary[]>({
    queryKey: ['chats'],
    queryFn: () => api.get('/admin/chats').then((r) => r.data),
    refetchInterval: 5000,
  })
  const [alertsOpen, setAlertsOpen] = useState(false)
  const { data: settings } = useQuery<{ whatsAppNumber: string | null; chatbotInquiryEnabled: boolean } & ChatAlertSettings>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  function open(phone: string | null) {
    setParams(phone ? { phone } : {}, { replace: !isTouch })
  }

  const inquiryOff = settings && !settings.chatbotInquiryEnabled && (
    <div className="mb-4 rounded-xl border border-line bg-surface px-4 py-3 text-sm text-muted">{t(lang, 'chatsInquiryOff')}</div>
  )
  // The page title with the ⚙️ that opens the alert settings.
  const header = (
    <div className="flex items-center justify-between gap-3 mb-4">
      <h1 className="text-2xl font-bold text-ink">{t(lang, 'navChats')}</h1>
      <button onClick={() => setAlertsOpen(true)} disabled={!settings} aria-label={t(lang, 'chatsSettings')} title={t(lang, 'chatsSettings')}
        className="flex items-center gap-2 border border-line bg-surface hover:bg-cream text-ink text-sm font-medium rounded-xl px-3 py-2 disabled:opacity-50">
        <span>⚙️</span><span>{t(lang, 'chatsSettings')}</span>
      </button>
      {alertsOpen && settings && <ChatAlertsModal lang={lang} settings={settings} onClose={() => setAlertsOpen(false)} />}
    </div>
  )
  const list = (
    <ChatList lang={lang} chats={chats} isLoading={isLoading} selected={selected} onOpen={open} />
  )
  const conversation = selected ? (
    <Conversation
      key={selected} lang={lang} phone={selected} linked={!!settings?.whatsAppNumber}
      unread={chats.find((c) => c.phone === selected)?.unread ?? 0}
      onBack={isTouch ? () => open(null) : undefined}
      onChanged={() => {
        queryClient.invalidateQueries({ queryKey: ['chats'] })
        queryClient.invalidateQueries({ queryKey: CHATS_UNREAD_KEY })
      }}
    />
  ) : (
    <div className="flex-1 flex items-center justify-center text-muted text-sm">{t(lang, 'chatsPick')}</div>
  )

  if (isTouch) {
    return (
      <div>
        {!selected && header}
        {!selected && inquiryOff}
        {selected ? conversation : list}
      </div>
    )
  }

  return (
    <div className="flex flex-col h-[calc(100vh-4rem)]">
      {header}
      {inquiryOff}
      <div className="flex-1 min-h-0 flex bg-surface border border-line rounded-2xl overflow-hidden">
        <div className="w-80 shrink-0 border-e border-line overflow-y-auto">{list}</div>
        <div className="flex-1 min-w-0 flex flex-col">{conversation}</div>
      </div>
    </div>
  )
}

function ChatList({ lang, chats, isLoading, selected, onOpen }: {
  lang: string; chats: ChatSummary[]; isLoading: boolean; selected: string | null; onOpen: (phone: string) => void
}) {
  if (isLoading) return <div className="text-muted py-12 text-center">{t(lang, 'loading')}</div>
  if (chats.length === 0) return <div className="text-muted py-12 px-4 text-center text-sm">{t(lang, 'chatsEmpty')}</div>

  return (
    <ul className="divide-y divide-line">
      {chats.map((c) => (
        <li key={c.phone}>
          <button
            onClick={() => onOpen(c.phone)}
            className={`w-full text-start px-4 py-3 flex items-start gap-3 transition-colors ${
              c.phone === selected ? 'bg-coral-tint' : 'hover:bg-cream'
            } ${c.isOpen ? '' : 'opacity-70'}`}
          >
            <div className="min-w-0 flex-1">
              <div className="flex items-center justify-between gap-2">
                <span className={`truncate text-sm ${c.unread > 0 ? 'font-bold text-ink' : 'font-medium text-ink'}`}>
                  {c.name ?? <bdi dir="ltr">{c.phone}</bdi>}
                </span>
                <span className="text-muted text-xs shrink-0">{when(c.lastAt, lang)}</span>
              </div>
              <div className="flex items-center justify-between gap-2 mt-0.5">
                <span className="truncate text-muted text-xs">
                  {c.lastSender !== 'Customer' && `${senderLabel(c.lastSender, lang)}: `}{c.lastText}
                </span>
                {c.unread > 0
                  ? <UnreadBadge count={c.unread} label={t(lang, 'chatsUnread').replace('{n}', String(c.unread))} />
                  : <StatusTag lang={lang} open={c.isOpen} />}
              </div>
            </div>
          </button>
        </li>
      ))}
    </ul>
  )
}

function StatusTag({ lang, open }: { lang: string; open: boolean }) {
  return (
    <span className={`shrink-0 text-[11px] px-1.5 py-0.5 rounded border ${open ? 'border-coral text-coral-dark' : 'border-line text-muted'}`}>
      {t(lang, open ? 'chatsOpenBadge' : 'chatsEndedBadge')}
    </span>
  )
}

function Conversation({ lang, phone, linked, unread, onBack, onChanged }: {
  lang: string; phone: string; linked: boolean; unread: number; onBack?: () => void; onChanged: () => void
}) {
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState(() => drafts.get(phone)?.text ?? '')
  const [file, setFile] = useState<File | null>(() => drafts.get(phone)?.file ?? null)
  useEffect(() => {
    if (draft || file) drafts.set(phone, { text: draft, file })
    else drafts.delete(phone)
  }, [phone, draft, file])
  const [readyOpen, setReadyOpen] = useState(false)
  const fileInput = useRef<HTMLInputElement>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [panel, setPanel] = useState<'none' | 'link' | 'finish'>('none')
  const [serviceId, setServiceId] = useState('')
  const [customerMenu, setCustomerMenu] = useState(false)
  const [booking, setBooking] = useState(false)
  const navigate = useNavigate()
  const bottomRef = useRef<HTMLDivElement>(null)

  const { data: chat, isLoading, isError } = useQuery<Chat>({
    queryKey: ['chat', phone],
    queryFn: () => api.get('/admin/chats/messages', { params: { phone } }).then((r) => r.data),
    refetchInterval: 3000,
    retry: false,
  })
  const { data: services = [] } = useQuery<Service[]>({
    queryKey: ['services'],
    queryFn: () => api.get('/admin/items').then((r) => r.data),
    select: (items) => items.filter((i) => i.isActive && i.isBookable),
  })

  // Opening the chat (and every new customer message while it's open) reads it.
  useEffect(() => {
    if (unread === 0) return
    api.post('/admin/chats/read', { phone }).then(onChanged).catch(() => {})
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phone, unread])

  const lastId = chat?.messages.at(-1)?.id
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ block: 'end' })
  }, [lastId])

  // Every action posts, then refreshes this chat and the list/badge.
  async function act(url: string, body: object, after?: () => void) {
    if (busy) return
    setBusy(true)
    setError(null)
    setNotice(null)
    try {
      await api.post(url, { phone, ...body })
      after?.()
      await queryClient.invalidateQueries({ queryKey: ['chat', phone] })
      onChanged()
    } catch (err) {
      setError(apiError(err) ?? t(lang, 'chatsSendError'))
    } finally {
      setBusy(false)
    }
  }

  // With a chosen file the text is its caption.
  async function send() {
    if (!file) {
      if (draft.trim()) await act('/admin/chats/messages', { text: draft.trim() }, () => { setDraft(''); drafts.delete(phone) })
      return
    }
    if (busy) return
    setBusy(true); setError(null); setNotice(null)
    try {
      const form = new FormData()
      form.append('phone', phone)
      form.append('caption', draft.trim())
      form.append('file', file)
      await api.post('/admin/chats/attachment', form)
      setDraft(''); setFile(null); drafts.delete(phone)
      await queryClient.invalidateQueries({ queryKey: ['chat', phone] })
      onChanged()
    } catch (err) {
      setError(apiError(err) ?? t(lang, 'chatsSendError'))
    } finally {
      setBusy(false)
    }
  }

  function pickFile(f: File | undefined) {
    if (fileInput.current) fileInput.current.value = ''
    if (!f) return
    if (f.size > MAX_FILE_BYTES) { setError(t(lang, 'chatsFileTooBig')); return }
    setError(null)
    setFile(f)
  }
  const sendLink = () => serviceId && act('/admin/chats/booking-link', { itemId: serviceId }, () => {
    setPanel('none'); setServiceId(''); setNotice(t(lang, 'chatsLinkSent'))
  })
  const finish = (sendServiceList: boolean) => act('/admin/chats/finish', { sendServiceList }, () => setPanel('none'))

  const canAct = linked && chat?.isOpen
  const selection = chat ? customerSelection(chat) : null
  const button = 'text-sm font-medium rounded-xl px-3 py-2 disabled:opacity-50'

  return (
    <div className={onBack ? 'flex flex-col h-[calc(100dvh-7rem)]' : 'flex flex-col h-full'}>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3 border-b border-line bg-surface">
        {onBack && (
          <button onClick={onBack} className="text-muted hover:text-ink text-sm shrink-0">
            <span className="rtl:rotate-180 inline-block">←</span> {t(lang, 'chatsAll')}
          </button>
        )}
        <button onClick={() => chat && setCustomerMenu(true)} disabled={!chat} title={t(lang, 'chatsCustomerActions')}
          className="min-w-0 flex-1 text-start rounded-lg -mx-1 px-1 hover:bg-cream disabled:hover:bg-transparent">
          <div className="text-ink font-medium truncate underline decoration-dotted underline-offset-4">{chat?.name ?? <bdi dir="ltr">{phone}</bdi>}</div>
          {chat?.name && <div className="text-muted text-xs"><bdi dir="ltr">{phone}</bdi></div>}
        </button>
        {chat?.isOpen && (
          <div className="flex gap-2">
            {canAct && (
              <button onClick={() => setPanel(panel === 'link' ? 'none' : 'link')} disabled={busy}
                className={`${button} border border-line text-ink hover:bg-cream`}>
                {t(lang, 'chatsSendLink')}
              </button>
            )}
            <button onClick={() => setPanel(panel === 'finish' ? 'none' : 'finish')} disabled={busy}
              className={`${button} border border-line text-ink hover:bg-cream`}>
              {t(lang, 'chatsFinish')}
            </button>
          </div>
        )}
        {chat && !chat.isOpen && <StatusTag lang={lang} open={false} />}
      </div>

      {customerMenu && chat && (
        <div onClick={() => setCustomerMenu(false)} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4">
          <div onClick={(e) => e.stopPropagation()} role="dialog" aria-modal="true"
            className="bg-surface rounded-2xl p-6 w-full max-w-sm border border-line">
            <div className="flex items-start justify-between gap-3 mb-1">
              <h2 className="text-ink font-semibold text-lg min-w-0 truncate">{chat.name ?? <bdi dir="ltr">{chat.phone}</bdi>}</h2>
              <button onClick={() => setCustomerMenu(false)} aria-label={t(lang, 'close')}
                className="-m-2 w-11 h-11 flex items-center justify-center rounded-lg text-muted hover:text-ink hover:bg-cream text-2xl leading-none">✕</button>
            </div>
            <p className="text-muted text-sm mb-5">{t(lang, 'chatsCustomerActions')}</p>
            <div className="space-y-2">
              <button onClick={() => { setCustomerMenu(false); setBooking(true) }}
                className="w-full bg-coral hover:bg-coral-dark text-white font-semibold py-2.5 rounded-lg">
                📅 {t(lang, 'chatsBookAppointment')}
              </button>
              <button onClick={() => navigate('/admin/recurring', { state: { customer: selection } })}
                className="w-full border border-coral text-coral-dark hover:bg-coral-tint font-semibold py-2.5 rounded-lg">
                🔁 {t(lang, 'chatsAddRecurring')}
              </button>
              <button onClick={() => navigate('/admin/appointments', { state: { search: chat.phone } })}
                className="w-full border border-line text-ink hover:bg-cream font-semibold py-2.5 rounded-lg">
                📋 {t(lang, 'chatsCustomerAppointments')}
              </button>
            </div>
          </div>
        </div>
      )}
      {readyOpen && (
        <ReadyMessagesModal lang={lang} phone={phone} onClose={() => setReadyOpen(false)}
          onPick={(text) => { setDraft((d) => (d.trim() ? `${d.trimEnd()}\n\n${text}` : text).slice(0, MAX_TEXT)); setReadyOpen(false) }} />
      )}
      {booking && selection && (
        <NewAppointmentModal lang={lang} initialCustomer={selection} onClose={() => setBooking(false)} />
      )}
      {canAct && panel === 'link' && (
        <div className="flex flex-wrap items-center gap-2 px-4 py-3 border-b border-line bg-surface">
          <select value={serviceId} onChange={(e) => setServiceId(e.target.value)}
            className="flex-1 min-w-40 rounded-xl border border-line bg-cream px-3 py-2 text-sm text-ink">
            <option value="">{t(lang, 'chatsPickService')}</option>
            {services.map((s) => <option key={s.id} value={s.id}>{itemName(s, lang)}</option>)}
          </select>
          <button onClick={sendLink} disabled={busy || !serviceId} className={`${button} bg-coral hover:bg-coral-dark text-white`}>
            {t(lang, 'chatsSend')}
          </button>
          <button onClick={() => setPanel('none')} className={`${button} text-muted hover:text-ink`}>{t(lang, 'chatsCancel')}</button>
        </div>
      )}
      {chat?.isOpen && panel === 'finish' && (
        <div className="px-4 py-3 border-b border-line bg-surface space-y-2">
          <div className="text-sm text-ink">{t(lang, 'chatsFinishQuestion')}</div>
          <div className="flex flex-wrap gap-2">
            <button onClick={() => finish(false)} disabled={busy} className={`${button} bg-coral hover:bg-coral-dark text-white`}>
              {t(lang, 'chatsFinishOnly')}
            </button>
            <button onClick={() => finish(true)} disabled={busy} className={`${button} border border-coral text-coral-dark hover:bg-coral-tint`}>
              {t(lang, 'chatsFinishWithServices')}
            </button>
            <button onClick={() => setPanel('none')} className={`${button} text-muted hover:text-ink`}>{t(lang, 'chatsCancel')}</button>
          </div>
        </div>
      )}

      <div className="flex-1 overflow-y-auto px-4 py-3 space-y-2 bg-cream">
        {isLoading && <div className="text-muted py-12 text-center">{t(lang, 'loading')}</div>}
        {!chat && isError && <div className="text-muted py-12 text-center text-sm">{t(lang, 'chatsNotFound')}</div>}
        {chat?.messages.map((m) => <Bubble key={m.id} m={m} lang={lang} />)}
        <div ref={bottomRef} />
      </div>

      {error && <div className="px-4 py-2 text-sm text-red-600 border-t border-line bg-surface">{error}</div>}
      {notice && <div className="px-4 py-2 text-sm text-ink border-t border-line bg-surface">✓ {notice}</div>}
      {!chat ? null : !linked ? (
        <div className="px-4 py-3 text-sm text-muted border-t border-line bg-surface">{t(lang, 'chatsNotLinked')}</div>
      ) : !chat.isOpen ? (
        <div className="px-4 py-3 text-sm text-muted border-t border-line bg-surface">{t(lang, 'chatsEnded')}</div>
      ) : (
        <div className="p-3 border-t border-line bg-surface">
          {file && (
            <div className="flex items-center gap-2 mb-2 rounded-lg border border-line bg-cream px-3 py-1.5 text-sm text-ink">
              <span className="shrink-0">{file.type.startsWith('image/') ? '🖼️' : '📄'}</span>
              <span className="truncate flex-1" dir="auto">{file.name}</span>
              <span className="text-muted text-xs shrink-0">{fileSize(file.size)}</span>
              <button onClick={() => setFile(null)} aria-label={t(lang, 'chatsRemoveFile')}
                className="shrink-0 w-7 h-7 flex items-center justify-center rounded text-muted hover:text-ink hover:bg-surface">✕</button>
            </div>
          )}
          <div className="flex items-end gap-2">
            <input ref={fileInput} type="file" accept={ACCEPTED_FILES} className="hidden"
              onChange={(e) => pickFile(e.target.files?.[0])} />
            <button onClick={() => fileInput.current?.click()} disabled={busy} aria-label={t(lang, 'chatsAttach')} title={t(lang, 'chatsAttach')}
              className="shrink-0 w-11 h-11 flex items-center justify-center rounded-xl border border-line text-lg hover:bg-cream disabled:opacity-50">
              📎
            </button>
            <button onClick={() => setReadyOpen(true)} disabled={busy} aria-label={t(lang, 'chatsReady')} title={t(lang, 'chatsReady')}
              className="shrink-0 w-11 h-11 flex items-center justify-center rounded-xl border border-line text-lg hover:bg-cream disabled:opacity-50">
              ⚡
            </button>
            <textarea
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              onKeyDown={(e) => {
                // Enter sends on a keyboard; on a phone Enter is a new line and the button sends.
                if (e.key === 'Enter' && !e.shiftKey && !onBack) { e.preventDefault(); send() }
              }}
              rows={2}
              maxLength={MAX_TEXT}
              dir="auto"
              placeholder={t(lang, 'chatsPlaceholder')}
              className="flex-1 resize-none rounded-xl border border-line bg-cream px-3 py-2 text-sm text-ink focus:outline-none focus:border-coral"
            />
            <button
              onClick={send}
              disabled={busy || (!draft.trim() && !file)}
              className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-medium rounded-xl px-4 py-2.5"
            >
              {t(lang, 'chatsSend')}
            </button>
          </div>
          <div className="text-muted text-[11px] mt-1.5">{t(lang, 'chatsOwnerLine')}</div>
        </div>
      )}
    </div>
  )
}

function Bubble({ m, lang }: { m: ChatMessage; lang: string }) {
  const mine = m.sender !== 'Customer'
  const style = m.sender === 'Customer'
    ? 'bg-surface text-ink border border-line'
    : m.sender === 'Bot'
      ? 'bg-surface text-ink border border-dashed border-line'
      : 'bg-coral text-white'
  return (
    <div className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
      <div className={`max-w-[80%] rounded-2xl px-3 py-2 ${style}`}>
        {mine && <div className={`text-[11px] mb-0.5 ${m.sender === 'Bot' ? 'text-muted' : 'text-white/80'}`}>{senderLabel(m.sender, lang)}</div>}
        {m.attachment && <AttachmentView attachment={m.attachment} light={m.sender !== 'Owner' && m.sender !== 'OwnerPhone'} />}
        {m.text && <div className="text-sm whitespace-pre-wrap break-words" dir="auto">{m.text}</div>}
        <div className={`text-[11px] mt-1 ${m.sender === 'Bot' || m.sender === 'Customer' ? 'text-muted' : 'text-white/80'}`}>
          {when(m.createdAt, lang)}
        </div>
      </div>
    </div>
  )
}

// A photo or file in the chat (the owner's or the customer's). Its bytes need the owner's token (not
// a public link), so it's fetched with the api client: a photo shows as a picture, a voice note or
// video plays here, anything else is a button that saves it.
function AttachmentView({ attachment, light }: { attachment: ChatAttachment; light: boolean }) {
  const kind = attachment.contentType.split('/')[0]
  const inline = kind === 'image' || kind === 'audio' || kind === 'video'
  // Its own object URL, revoked when the bubble goes away: cached ones were never freed, so
  // memory grew over a long session (QA run 5).
  const [url, setUrl] = useState<string>()
  useEffect(() => {
    if (!inline) return
    let href: string | undefined
    let cancelled = false
    api.get(`/admin/chats/attachments/${attachment.id}`, { responseType: 'blob' })
      .then((r) => {
        href = URL.createObjectURL(r.data as Blob)
        if (cancelled) URL.revokeObjectURL(href)
        else setUrl(href)
      })
      .catch(() => {})
    return () => {
      cancelled = true
      if (href) URL.revokeObjectURL(href)
    }
  }, [attachment.id, inline])

  const [downloadFailed, setDownloadFailed] = useState(false)
  async function download() {
    setDownloadFailed(false)
    let r
    try {
      r = await api.get(`/admin/chats/attachments/${attachment.id}`, { responseType: 'blob' })
    } catch {
      setDownloadFailed(true) // nothing happened at all before (QA run 6)
      return
    }
    const href = URL.createObjectURL(r.data as Blob)
    const a = document.createElement('a')
    a.href = href
    a.download = attachment.fileName
    a.click()
    setTimeout(() => URL.revokeObjectURL(href), 1000)
  }

  if (inline && !url) return <div className={`${kind === 'audio' ? 'w-60 h-10' : 'w-48 h-32'} rounded-lg mb-1 bg-black/10`} />
  if (kind === 'image')
    return <a href={url} target="_blank" rel="noreferrer"><img src={url} alt={attachment.fileName} className="rounded-lg max-h-64 mb-1" /></a>
  if (kind === 'audio') return <audio controls src={url} className="max-w-full mb-1" />
  if (kind === 'video') return <video controls src={url} className="rounded-lg max-h-64 max-w-full mb-1" />
  return (
    <button onClick={download}
      className={`flex items-center gap-2 mb-1 rounded-lg px-2 py-1.5 text-sm text-start ${light ? 'bg-cream text-ink' : 'bg-white/20 text-white'}`}>
      <span>📄</span><span className="underline break-all" dir="auto">{attachment.fileName}</span>
      {downloadFailed && <span className="text-xs">⚠️</span>}
    </button>
  )
}

function fileSize(bytes: number) {
  return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`
}

// The chat's customer for the booking forms: their customer record when they have one here, else
// a new customer with the WhatsApp name (first word, rest as the family name) and phone, editable.
function customerSelection(chat: Chat): CustomerSelection {
  if (chat.customerId) return { customerId: chat.customerId, label: `${chat.name ?? ''} · ${chat.phone}` }
  const [first = '', ...rest] = (chat.name ?? '').trim().split(/\s+/)
  return { customerName: first, customerFamilyName: rest.join(' '), customerPhone: chat.phone }
}

function senderLabel(sender: Sender, lang: string) {
  if (sender === 'Bot') return t(lang, 'chatsSenderBot')
  if (sender === 'OwnerPhone') return t(lang, 'chatsSenderPhone')
  return t(lang, 'chatsSenderYou')
}

function when(iso: string, lang: string) {
  const d = parseISO(iso)
  return format(d, isToday(d) ? 'HH:mm' : 'd MMM HH:mm', { locale: dateLocale(lang) })
}
