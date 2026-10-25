import { useEffect, useRef, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { format, isToday, parseISO } from 'date-fns'
import { dateLocale } from '../../lib/dateLocale'
import { api } from '../../lib/api'
import { apiError } from '../../lib/apiError'
import { useAuth } from '../../lib/auth'
import { itemName, t } from '../../lib/i18n'
import { useIsTouchPrimary } from '../../lib/useIsTouchPrimary'
import UnreadBadge, { CHATS_UNREAD_KEY } from '../../components/admin/UnreadBadge'

type Sender = 'Customer' | 'Bot' | 'Owner' | 'OwnerPhone'
type ChatSummary = { phone: string; name: string | null; lastText: string; lastSender: Sender; lastAt: string; unread: number; isOpen: boolean }
type ChatMessage = { id: string; sender: Sender; text: string; createdAt: string }
type Chat = { phone: string; name: string | null; isOpen: boolean; messages: ChatMessage[] }
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
  const { data: settings } = useQuery<{ whatsAppNumber: string | null; chatbotInquiryEnabled: boolean }>({
    queryKey: ['settings'],
    queryFn: () => api.get('/admin/settings').then((r) => r.data),
  })

  function open(phone: string | null) {
    setParams(phone ? { phone } : {}, { replace: !isTouch })
  }

  const inquiryOff = settings && !settings.chatbotInquiryEnabled && (
    <div className="mb-4 rounded-xl border border-line bg-surface px-4 py-3 text-sm text-muted">{t(lang, 'chatsInquiryOff')}</div>
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
        {!selected && <h1 className="text-2xl font-bold text-ink mb-4">{t(lang, 'navChats')}</h1>}
        {!selected && inquiryOff}
        {selected ? conversation : list}
      </div>
    )
  }

  return (
    <div className="flex flex-col h-[calc(100vh-4rem)]">
      <h1 className="text-2xl font-bold text-ink mb-4">{t(lang, 'navChats')}</h1>
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
  const [draft, setDraft] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notice, setNotice] = useState<string | null>(null)
  const [panel, setPanel] = useState<'none' | 'link' | 'finish'>('none')
  const [serviceId, setServiceId] = useState('')
  const bottomRef = useRef<HTMLDivElement>(null)

  const { data: chat, isLoading } = useQuery<Chat>({
    queryKey: ['chat', phone],
    queryFn: () => api.get('/admin/chats/messages', { params: { phone } }).then((r) => r.data),
    refetchInterval: 3000,
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

  const send = () => draft.trim() && act('/admin/chats/messages', { text: draft.trim() }, () => setDraft(''))
  const sendLink = () => serviceId && act('/admin/chats/booking-link', { itemId: serviceId }, () => {
    setPanel('none'); setServiceId(''); setNotice(t(lang, 'chatsLinkSent'))
  })
  const finish = (sendServiceList: boolean) => act('/admin/chats/finish', { sendServiceList }, () => setPanel('none'))

  const canAct = linked && chat?.isOpen
  const button = 'text-sm font-medium rounded-xl px-3 py-2 disabled:opacity-50'

  return (
    <div className={onBack ? 'flex flex-col h-[calc(100dvh-7rem)]' : 'flex flex-col h-full'}>
      <div className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 py-3 border-b border-line bg-surface">
        {onBack && (
          <button onClick={onBack} className="text-muted hover:text-ink text-sm shrink-0">
            <span className="rtl:rotate-180 inline-block">←</span> {t(lang, 'chatsAll')}
          </button>
        )}
        <div className="min-w-0 flex-1">
          <div className="text-ink font-medium truncate">{chat?.name ?? <bdi dir="ltr">{phone}</bdi>}</div>
          {chat?.name && <div className="text-muted text-xs"><bdi dir="ltr">{phone}</bdi></div>}
        </div>
        {canAct && (
          <div className="flex gap-2">
            <button onClick={() => setPanel(panel === 'link' ? 'none' : 'link')} disabled={busy}
              className={`${button} border border-line text-ink hover:bg-cream`}>
              {t(lang, 'chatsSendLink')}
            </button>
            <button onClick={() => setPanel(panel === 'finish' ? 'none' : 'finish')} disabled={busy}
              className={`${button} border border-line text-ink hover:bg-cream`}>
              {t(lang, 'chatsFinish')}
            </button>
          </div>
        )}
        {chat && !chat.isOpen && <StatusTag lang={lang} open={false} />}
      </div>

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
      {canAct && panel === 'finish' && (
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
        {chat?.messages.map((m) => <Bubble key={m.id} m={m} lang={lang} />)}
        <div ref={bottomRef} />
      </div>

      {error && <div className="px-4 py-2 text-sm text-red-600 border-t border-line bg-surface">{error}</div>}
      {notice && <div className="px-4 py-2 text-sm text-ink border-t border-line bg-surface">✓ {notice}</div>}
      {!linked ? (
        <div className="px-4 py-3 text-sm text-muted border-t border-line bg-surface">{t(lang, 'chatsNotLinked')}</div>
      ) : chat && !chat.isOpen ? (
        <div className="px-4 py-3 text-sm text-muted border-t border-line bg-surface">{t(lang, 'chatsEnded')}</div>
      ) : (
        <div className="p-3 border-t border-line bg-surface">
          <div className="flex items-end gap-2">
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
              disabled={busy || !draft.trim()}
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
