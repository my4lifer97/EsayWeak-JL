import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, isToday, parseISO } from 'date-fns'
import { dateLocale } from '../../lib/dateLocale'
import { api } from '../../lib/api'
import { apiError } from '../../lib/apiError'
import { useAuth } from '../../lib/auth'
import { t } from '../../lib/i18n'
import { useIsTouchPrimary } from '../../lib/useIsTouchPrimary'
import UnreadBadge, { CHATS_UNREAD_KEY } from '../../components/admin/UnreadBadge'

type Sender = 'Customer' | 'Bot' | 'Owner' | 'OwnerPhone'
type ChatSummary = { phone: string; name: string | null; lastText: string; lastSender: Sender; lastAt: string; unread: number }
type ChatMessage = { id: string; sender: Sender; text: string; createdAt: string }
type Chat = { phone: string; name: string | null; botPausedUntil: string | null; messages: ChatMessage[] }

// The WhatsApp conversations of the business's linked number, and replying to a customer from
// here instead of the phone (ChatsController). Polls, since messages arrive from WhatsApp.
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
  const { data: settings } = useQuery<{ whatsAppNumber: string | null }>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  function open(phone: string | null) {
    setParams(phone ? { phone } : {}, { replace: !isTouch })
  }

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
        {!selected && <h1 className="text-2xl font-bold text-ink mb-4">{t(lang, 'navChats')}</h1>}
        {selected ? conversation : list}
      </div>
    )
  }

  return (
    <div className="flex flex-col h-[calc(100vh-4rem)]">
      <h1 className="text-2xl font-bold text-ink mb-4">{t(lang, 'navChats')}</h1>
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
            }`}
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
                {c.unread > 0 && <UnreadBadge count={c.unread} label={t(lang, 'chatsUnread').replace('{n}', String(c.unread))} />}
              </div>
            </div>
          </button>
        </li>
      ))}
    </ul>
  )
}

function Conversation({ lang, phone, linked, unread, onBack, onChanged }: {
  lang: string; phone: string; linked: boolean; unread: number; onBack?: () => void; onChanged: () => void
}) {
  const queryClient = useQueryClient()
  const [draft, setDraft] = useState('')
  const [sending, setSending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const bottomRef = useRef<HTMLDivElement>(null)

  const { data: chat, isLoading } = useQuery<Chat>({
    queryKey: ['chat', phone],
    queryFn: () => api.get('/admin/chats/messages', { params: { phone } }).then((r) => r.data),
    refetchInterval: 3000,
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

  async function send() {
    const text = draft.trim()
    if (!text || sending) return
    setSending(true)
    setError(null)
    try {
      await api.post('/admin/chats/messages', { phone, text })
      setDraft('')
      await queryClient.invalidateQueries({ queryKey: ['chat', phone] })
      onChanged()
    } catch (err) {
      setError(apiError(err) ?? t(lang, 'chatsSendError'))
    } finally {
      setSending(false)
    }
  }

  return (
    <div className={onBack ? 'flex flex-col h-[calc(100dvh-7rem)]' : 'flex flex-col h-full'}>
      <div className="flex items-center gap-3 px-4 py-3 border-b border-line bg-surface">
        {onBack && (
          <button onClick={onBack} className="text-muted hover:text-ink text-sm shrink-0">
            <span className="rtl:rotate-180 inline-block">←</span> {t(lang, 'chatsAll')}
          </button>
        )}
        <div className="min-w-0">
          <div className="text-ink font-medium truncate">{chat?.name ?? <bdi dir="ltr">{phone}</bdi>}</div>
          {chat?.name && <div className="text-muted text-xs"><bdi dir="ltr">{phone}</bdi></div>}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-3 space-y-2 bg-cream">
        {isLoading && <div className="text-muted py-12 text-center">{t(lang, 'loading')}</div>}
        {chat?.messages.map((m) => <Bubble key={m.id} m={m} lang={lang} />)}
        <div ref={bottomRef} />
      </div>

      {chat?.botPausedUntil && (
        <div className="px-4 py-2 text-xs text-muted border-t border-line bg-surface">
          {t(lang, 'chatsBotPaused').replace('{time}', format(parseISO(chat.botPausedUntil), 'HH:mm'))}
        </div>
      )}
      {error && <div className="px-4 py-2 text-sm text-red-600 border-t border-line bg-surface">{error}</div>}
      {linked ? (
        <div className="flex items-end gap-2 p-3 border-t border-line bg-surface">
          <textarea
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => {
              // Enter sends on a keyboard; on a phone Enter is a new line and the button sends.
              if (e.key === 'Enter' && !e.shiftKey && !onBack) { e.preventDefault(); send() }
            }}
            rows={2}
            maxLength={4000}
            placeholder={t(lang, 'chatsPlaceholder')}
            className="flex-1 resize-none rounded-xl border border-line bg-cream px-3 py-2 text-sm text-ink focus:outline-none focus:border-coral"
          />
          <button
            onClick={send}
            disabled={sending || !draft.trim()}
            className="bg-coral hover:bg-coral-dark disabled:opacity-50 text-white text-sm font-medium rounded-xl px-4 py-2.5"
          >
            {t(lang, 'chatsSend')}
          </button>
        </div>
      ) : (
        <div className="px-4 py-3 text-sm text-muted border-t border-line bg-surface">{t(lang, 'chatsNotLinked')}</div>
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
        <div className="text-sm whitespace-pre-wrap break-words" dir="auto">{m.text}</div>
        <div className={`text-[11px] mt-1 ${m.sender === 'Bot' || m.sender === 'Customer' ? 'text-muted' : 'text-white/80'}`}>
          {when(m.createdAt, lang)}
        </div>
      </div>
    </div>
  )
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
