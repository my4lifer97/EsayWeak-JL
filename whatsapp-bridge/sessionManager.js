import { promises as fs } from 'fs'
import { randomBytes } from 'crypto'
import path from 'path'
import makeWASocket, { useMultiFileAuthState, fetchLatestBaileysVersion, DisconnectReason, generateMessageIDV2, downloadMediaMessage, extractMessageContent } from '@whiskeysockets/baileys'
import { pino } from 'pino'
import QRCode from 'qrcode'

const SESSIONS_DIR = process.env.SESSIONS_DIR || './data/sessions'
const BACKEND_URL = process.env.BACKEND_URL
const BRIDGE_SECRET = process.env.BRIDGE_SECRET

// businessId -> { sock, status: 'connecting'|'qr'|'connected'|'disconnected', qr, phoneNumber, unlinking? }
// One process, many sockets -- Baileys sessions are just websocket connections, not separate OS
// processes, so this comfortably holds every business's linked number.
const sessions = new Map()
// businessId -> reconnect attempts since the last successful open, for the backoff below.
const failures = new Map()
// Ids of the messages this bridge sent (chatbot replies, backend sends). WhatsApp echoes them back
// as fromMe like the ones the owner types on the phone, and only the latter are reported.
const sentIds = new Set()
const MAX_SENT_IDS = 5000
// A customer's photo, video, voice note, sticker or file goes to the backend with the message, for
// the owner's Chats page (kept only while that customer's chat is open). Bigger ones are reported
// by name only, so one long video can't fill the database.
// Calls to the backend give up after this, so one slow reply can't hold up every later message
// (messages are handled one at a time, QA run 6).
const BACKEND_TIMEOUT_MS = 60_000
const MEDIA_TYPES = ['imageMessage', 'videoMessage', 'ptvMessage', 'audioMessage', 'documentMessage', 'stickerMessage']
const MAX_MEDIA_BYTES = 16 * 1024 * 1024
// A stalled download or send held up that business's whole inbound loop, which handles one
// message at a time (QA run 7).
const WHATSAPP_TIMEOUT_MS = 60_000

function withTimeout(promise, what, onTimeout) {
  let timer
  const timeout = new Promise((_, reject) => {
    timer = setTimeout(() => {
      onTimeout?.()
      reject(new Error(`${what} timed out`))
    }, WHATSAPP_TIMEOUT_MS)
  })
  return Promise.race([promise, timeout]).finally(() => clearTimeout(timer))
}
const silentLogger = pino({ level: 'silent' })

// The id is chosen and remembered before sending: Baileys echoes the message (messages.upsert)
// on process.nextTick, which can run before sendMessage's promise resolves.
// With media ({ base64, mimetype, fileName }, the owner's Chats page) the text is its caption:
// an image is sent as a photo, anything else as a document.
async function sendTracked(sock, jid, text, media) {
  const messageId = generateMessageIDV2(sock.user?.id)
  sentIds.add(messageId)
  if (sentIds.size > MAX_SENT_IDS) sentIds.delete(sentIds.values().next().value)
  let content = { text }
  if (media) {
    const data = Buffer.from(media.base64, 'base64')
    content = media.mimetype?.startsWith('image/')
      ? { image: data, mimetype: media.mimetype, caption: text }
      : { document: data, mimetype: media.mimetype, fileName: media.fileName, caption: text }
  }
  await withTimeout(sock.sendMessage(jid, content, { messageId }), 'sending a message')
}

export function statusDto(businessId) {
  const s = sessions.get(businessId)
  if (!s) return { state: 'disconnected', qr: null, phoneNumber: null }
  return { state: s.status, qr: s.qr, phoneNumber: s.phoneNumber }
}

export function getSocket(businessId) {
  return sessions.get(businessId)
}

// Boot-time reconnect: every business folder under SESSIONS_DIR has saved creds from a previous
// link, so redeploys don't force every business to re-scan a QR code (SESSIONS_DIR must be a
// persistent volume in production -- see README.md).
export async function restoreSessions() {
  const dirs = await fs.readdir(SESSIONS_DIR).catch(() => [])
  for (const businessId of dirs) {
    // A folder without linked creds is a QR that was never scanned: restoring it just shows a QR
    // nobody is looking at, again and again (production looped like that every 3 s).
    if (!(await hasLinkedCreds(businessId))) {
      console.log(`Skipping business ${businessId}: never linked, removing its empty session folder`)
      await fs.rm(path.join(SESSIONS_DIR, businessId), { recursive: true, force: true }).catch(() => {})
      continue
    }
    console.log(`Restoring WhatsApp session for business ${businessId}`)
    await startSession(businessId).catch((err) => console.error(`Failed to restore session ${businessId}:`, err.message))
  }
}

async function hasLinkedCreds(businessId) {
  try {
    const creds = JSON.parse(await fs.readFile(path.join(SESSIONS_DIR, businessId, 'creds.json'), 'utf8'))
    return !!creds.me
  } catch {
    return false
  }
}

// One start at a time per business: the checks above the first await let a double Link click, or
// a click during a reconnect, open two sockets on one auth folder -- every message answered twice
// and a "connection replaced" loop (QA run 6). A second caller gets the same start.
const starting = new Map()
// Bumped by every unlink. A start that settles after the unlink gave up waiting for it (60 s)
// still registered a session and showed a scannable QR (QA run 9); it checks this first.
const unlinkCount = new Map()

// A business id is a database id: letters, digits, "-" and "_" only, since it names a folder.
export function isValidBusinessId(businessId) {
  return typeof businessId === 'string' && /^[A-Za-z0-9_-]{1,64}$/.test(businessId)
}

export function startSession(businessId) {
  if (!isValidBusinessId(businessId)) return Promise.reject(new Error('Invalid business id'))
  const inFlight = starting.get(businessId)
  if (inFlight) return inFlight
  const start = startSessionNow(businessId).finally(() => starting.delete(businessId))
  starting.set(businessId, start)
  return start
}

async function startSessionNow(businessId) {
  const existing = sessions.get(businessId)
  if (existing && existing.status !== 'disconnected') return existing

  const unlinksAtStart = unlinkCount.get(businessId) ?? 0
  const authFolder = path.join(SESSIONS_DIR, businessId)
  const { state, saveCreds } = await useMultiFileAuthState(authFolder)
  const { version } = await fetchLatestBaileysVersion()
  if ((unlinkCount.get(businessId) ?? 0) !== unlinksAtStart) {
    await fs.rm(authFolder, { recursive: true, force: true }).catch(() => {})
    throw new Error('the business was unlinked while its session was starting')
  }

  const sock = makeWASocket({
    version,
    auth: state,
    logger: pino({ level: 'silent' }),
    printQRInTerminal: false,
  })

  const session = { sock, status: 'connecting', qr: null, phoneNumber: null }
  sessions.set(businessId, session)

  sock.ev.on('creds.update', saveCreds)

  // Since late July 2026 WhatsApp sends `companion_reg_refresh` while a QR is on screen and expects
  // a new adv secret in the QR. Baileys rc14 only acks it, so the phone scans a QR with a retired
  // secret and says "Couldn't link device / check your connection" (Baileys issue #2737, unmerged
  // fix PR #2765, done the same way here). Pair-success signs with creds.advSecretKey, so changing
  // it there is enough; Baileys keeps putting the old one in later QRs, hence withCurrentSecret.
  let lastQr = null
  const withCurrentSecret = (qr) => {
    const parts = qr.split(',')
    parts[parts.length - 2] = state.creds.advSecretKey // ref,noiseKey,identityKey,advSecret,platform
    return parts.join(',')
  }
  sock.ws.on('CB:notification,type:companion_reg_refresh', async () => {
    if (state.creds.me) return // already linked: the secret is what the session is verified with
    state.creds.advSecretKey = randomBytes(32).toString('base64')
    await saveCreds()
    console.log(`[${businessId}] WhatsApp rotated the pairing secret -- re-rendering the QR`)
    if (lastQr && session.status === 'qr') session.qr = await QRCode.toDataURL(withCurrentSecret(lastQr))
  })

  sock.ev.on('connection.update', async (update) => {
    const { connection, lastDisconnect, qr } = update

    if (qr) {
      // Once per session. Right after a scan's restart it means the saved creds were lost.
      if (session.status !== 'qr') console.log(`[${businessId}] waiting for a QR scan`)
      lastQr = qr
      session.qr = await QRCode.toDataURL(withCurrentSecret(qr))
      session.status = 'qr'
    }

    // A socket that already closed never comes back: right after a QR scan Baileys reported "open"
    // just after the restart close (same millisecond), which left the session marked connected,
    // so the reconnect below returned it as-is and the bot sat on a dead socket (2026-10-25).
    if (connection === 'open' && !session.closed) {
      failures.delete(businessId)
      session.status = 'connected'
      session.qr = null
      session.phoneNumber = sock.user?.id ? `+${sock.user.id.split(':')[0]}` : null
      console.log(`[${businessId}] WhatsApp connected as ${maskPhone(session.phoneNumber)}`)
      // The backend saves the number (Business.WhatsAppNumber), which confirmations, reminders and
      // the Chats page rely on -- also when nobody is watching a QR link (restart, reconnect).
      if (session.phoneNumber) {
        const phone = session.phoneNumber
        reportSessionEvent(businessId, 'connected', phone,
          () => sessions.get(businessId) === session && session.status === 'connected' && session.phoneNumber === phone)
      }
    }

    if (connection === 'close') {
      session.closed = true
      const statusCode = lastDisconnect?.error?.output?.statusCode
      const loggedOut = statusCode === DisconnectReason.loggedOut
      // Never linked (no `me` in the creds) and not the restart WhatsApp asks for right after a
      // scan: the QR expired unscanned ("QR refs attempts ended", 408). Reconnecting would start
      // a fresh QR every 3 s forever; stop instead, and the admin clicks Link again.
      const unlinkedQrExpired = !state.creds.me && statusCode !== DisconnectReason.restartRequired
      if (loggedOut || unlinkedQrExpired) {
        // Only while this socket is still the business's session: a newer one (Link clicked
        // again) owns the folder now, and deleting it wiped that pairing mid-scan (2026-10-25:
        // linked, then the restart after the scan found no creds and the bot never came back).
        if (sessions.get(businessId) !== session) return
        console.log(`[${businessId}] ${loggedOut ? 'logged out' : 'QR code not scanned in time'} -- clearing saved session`)
        // Still in the map while the folder goes, so a Link click in between gets this closing
        // session back instead of starting a new one on the folder being deleted.
        session.status = 'closing'
        session.qr = null
        failures.delete(businessId)
        await fs.rm(authFolder, { recursive: true, force: true }).catch(() => {})
        // Rechecked after the await: a Link click meanwhile may have put a newer session in the
        // map, which this one must not drop (QA run 5, like 26171aa).
        if (sessions.get(businessId) === session) sessions.delete(businessId)
        // WhatsApp ended a linked session: tell the backend so the platform admin gets an email.
        // Not for an admin's own Unlink (unlinkSession), which logs out on purpose.
        if (loggedOut && !session.unlinking)
          reportSessionEvent(businessId, 'loggedOut', session.phoneNumber, () => !sessions.has(businessId))
      } else {
        session.status = 'disconnected'
        // DisconnectReason.forbidden means WhatsApp is actively (and temporarily) blocking this
        // connection -- retrying every few seconds during that window looks like exactly the
        // bot-like reconnect-hammering pattern that gets a block escalated into a full logout.
        // `error.data.expire` (unix seconds), when present, says when the block actually lifts;
        // otherwise back off a full minute rather than guessing low.
        const forbidden = statusCode === DisconnectReason.forbidden
        const expireAt = lastDisconnect?.error?.data?.expire
        // Otherwise 3 s, doubling on each failure in a row up to 5 minutes, so a number that
        // can't connect doesn't hammer WhatsApp from the IP every linked business shares.
        // The restart WhatsApp asks for right after a scan isn't a failure.
        const attempt = failures.get(businessId) ?? 0
        if (statusCode !== DisconnectReason.restartRequired) failures.set(businessId, attempt + 1)
        const retryMs = statusCode === DisconnectReason.restartRequired ? 3000 : Math.min(3000 * 2 ** attempt, 5 * 60_000)
        const delayMs = forbidden ? Math.max((expireAt ? expireAt * 1000 - Date.now() : 60_000), 60_000) : retryMs
        console.log(`[${businessId}] connection closed, reconnecting in ${Math.round(delayMs / 1000)}s (code ${statusCode}${forbidden ? ', WhatsApp temporary block' : ''})`)
        // Skipped if it was unlinked or restarted meanwhile (that session handles itself).
        // The status is set again here: startSession reuses any session that isn't 'disconnected'.
        // A failed start (e.g. the version lookup's network call) is retried later: without the
        // catch it was an unhandled rejection that could take down the bridge for every
        // business, or leave this one disconnected for good (QA run 6).
        const reconnect = () => {
          if (sessions.get(businessId) !== session) return
          session.status = 'disconnected'
          startSession(businessId).catch((err) => {
            console.error(`[${businessId}] reconnect failed, trying again in 60s:`, err.message)
            setTimeout(reconnect, 60_000)
          })
        }
        setTimeout(reconnect, delayMs)
      }
    }
  })

  sock.ev.on('messages.upsert', async ({ messages, type }) => {
    for (const msg of messages) {
      if (msg.key.fromMe) {
        await reportOwnMessage(businessId, sock, msg).catch((err) =>
          console.error(`[${businessId}] failed to report a message sent from the phone:`, err.message))
        continue
      }
      if (type !== 'notify') continue
      await handleInboundMessage(businessId, sock, msg).catch((err) =>
        console.error(`[${businessId}] failed to process inbound message:`, err.message))
    }
  })

  return session
}

// WhatsApp increasingly addresses 1:1 chats by an opaque "LID" (Linked ID) instead of the
// phone-based JID, for privacy -- remoteJid can then be e.g. "103873935073359@lid", which is NOT
// a phone number (confirmed in production: a real customer's booking got created with phone
// "+103873935073359"). remoteJidAlt carries the real phone-based JID when Baileys already knows
// it; signalRepository.lidMapping is the fallback for a LID Baileys has seen before but didn't
// attach directly to this message. If neither resolves, there is no way to recover a real phone
// number for this contact -- see https://baileys.wiki/concepts/jids.
async function resolvePhoneJid(sock, msg) {
  const jid = msg.key.remoteJid
  if (!jid.endsWith('@lid')) return jid
  if (msg.key.remoteJidAlt) return msg.key.remoteJidAlt
  try {
    const pn = await sock.signalRepository.lidMapping.getPNForLID(jid)
    if (pn) return pn
  } catch { /* no mapping known yet */ }
  return null
}

// View-once and disappearing messages wrap the real content; extractMessageContent unwraps them.
function messageText(msg) {
  const content = extractMessageContent(msg.message)
  return content?.conversation
    || content?.extendedTextMessage?.text
    || content?.imageMessage?.caption
    || content?.videoMessage?.caption
    || content?.documentMessage?.caption
    || ''
}

// { type, mimetype, fileName, base64 } for a message with media, else null. base64 is null when the
// file isn't wanted (the backend keeps files only in an open chat, within a quota), is too big or
// couldn't be downloaded -- the owner then sees its name and opens it on the phone.
function messageMediaInfo(msg) {
  const content = extractMessageContent(msg.message)
  const type = MEDIA_TYPES.find((t) => content?.[t])
  if (!type) return null
  const info = content[type]
  return { type: type.replace('Message', ''), mimetype: info.mimetype || null, fileName: info.fileName || null, base64: null, fileLength: Number(info.fileLength || 0) }
}

async function messageMedia(businessId, sock, msg, info, wanted) {
  const { fileLength, ...media } = info
  if (!wanted || fileLength > MAX_MEDIA_BYTES) return media
  try {
    // Streamed and stopped at the limit: fileLength is what the sender claims, and a whole-file
    // buffer of a bigger one could run the bridge out of memory (QA run 5).
    let stream
    const download = (async () => {
      stream = await downloadMediaMessage(msg, 'stream', {}, { reuploadRequest: sock.updateMediaMessage, logger: silentLogger })
      const chunks = []
      let size = 0
      for await (const chunk of stream) {
        size += chunk.length
        if (size > MAX_MEDIA_BYTES) {
          stream.destroy()
          return null
        }
        chunks.push(chunk)
      }
      return Buffer.concat(chunks)
    })()
    download.catch(() => {}) // still settles after a timeout; nothing waits for it then
    const data = await withTimeout(download, 'downloading media', () => stream?.destroy())
    if (data) media.base64 = data.toString('base64')
  } catch (err) {
    console.error(`[${businessId}] could not download a customer's ${media.type}:`, err.message)
  }
  return media
}

// Whether the backend will keep this customer's file. False on any error: the message still goes
// through, with the file's name only.
async function mediaWanted(businessId, fromPhone) {
  try {
    const resp = await fetch(`${BACKEND_URL}/api/whatsapp/bridge/media-wanted`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Bridge-Secret': BRIDGE_SECRET },
      signal: AbortSignal.timeout(BACKEND_TIMEOUT_MS),
      body: JSON.stringify({ businessId, fromPhone }),
    })
    return resp.ok && (await resp.json()).wanted === true
  } catch {
    return false
  }
}

// A message the owner typed on the linked phone to a customer: the backend shows it on the
// dashboard's Chats page and keeps the chatbot out of that conversation for a while.
// Not ours (sentIds), not a group, not the owner's chat with themselves, and only fresh ones --
// a reconnect can replay older messages.
async function reportOwnMessage(businessId, sock, msg) {
  if (!msg.message || sentIds.has(msg.key.id)) return
  const remote = msg.key.remoteJid
  if (!remote || remote.endsWith('@g.us') || remote.endsWith('@broadcast') || remote.endsWith('@newsletter')) return
  const text = messageText(msg)
  if (!text.trim()) return
  const sentAt = Number(msg.messageTimestamp || 0) * 1000
  if (sentAt && Date.now() - sentAt > 2 * 60_000) return

  const jid = await resolvePhoneJid(sock, msg)
  if (!jid) return
  const toPhone = `+${jid.split('@')[0].split(':')[0]}`
  const ownPhone = sock.user?.id ? `+${sock.user.id.split(':')[0].split('@')[0]}` : null
  if (toPhone === ownPhone) return

  const resp = await fetch(`${BACKEND_URL}/api/whatsapp/bridge/outgoing`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Bridge-Secret': BRIDGE_SECRET },
      signal: AbortSignal.timeout(BACKEND_TIMEOUT_MS),
    body: JSON.stringify({ businessId, toPhone, message: text }),
  })
  if (!resp.ok) throw new Error(`backend responded ${resp.status}`)
}

async function handleInboundMessage(businessId, sock, msg) {
  if (!msg.message || msg.key.fromMe) return
  const remote = msg.key.remoteJid
  // Groups, status posts and channels aren't conversations with a customer -- chatbot is 1:1 only.
  if (!remote || remote.endsWith('@g.us') || remote.endsWith('@broadcast') || remote.endsWith('@newsletter')) return

  const text = messageText(msg)
  const mediaInfo = messageMediaInfo(msg)
  if (!text.trim() && !mediaInfo) return

  const jid = await resolvePhoneJid(sock, msg)
  if (!jid) {
    console.error(`[${businessId}] could not resolve a real phone number for a LID -- dropping message rather than saving a bogus phone`)
    return
  }

  // Without a ":device" suffix ("15551234567:12"), like the owner's own messages (QA run 6).
  const fromPhone = `+${jid.split('@')[0].split(':')[0]}`
  const profileName = msg.pushName || null
  const media = mediaInfo && await messageMedia(businessId, sock, msg, mediaInfo, await mediaWanted(businessId, fromPhone))

  const resp = await fetch(`${BACKEND_URL}/api/whatsapp/bridge/inbound`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Bridge-Secret': BRIDGE_SECRET },
      signal: AbortSignal.timeout(BACKEND_TIMEOUT_MS),
    body: JSON.stringify({ businessId, fromPhone, profileName, message: text, media }),
  })
  if (!resp.ok) throw new Error(`backend responded ${resp.status}`)
  const { reply } = await resp.json()
  if (reply) await sendTracked(sock, jid, reply)
}

// 'loggedOut' (WhatsApp ended the session) or 'connected'. Best-effort, a few tries a minute
// apart in case the backend is mid-deploy.
// isCurrent: checked before each retry, so a late retry doesn't overwrite a newer logout or
// re-link the backend already heard about (QA run 5).
// Phone numbers in logs keep only their last 3 digits (QA run 5).
function maskPhone(phone) {
  return phone ? `***${String(phone).slice(-3)}` : phone
}

async function reportSessionEvent(businessId, event, phoneNumber, isCurrent = () => true, attempt = 1) {
  if (attempt > 1 && !isCurrent()) return
  try {
    const resp = await fetch(`${BACKEND_URL}/api/whatsapp/bridge/session-event`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Bridge-Secret': BRIDGE_SECRET },
      signal: AbortSignal.timeout(BACKEND_TIMEOUT_MS),
      body: JSON.stringify({ businessId, event, phoneNumber }),
    })
    if (resp.ok || resp.status === 404) return
    throw new Error(`backend responded ${resp.status}`)
  } catch (err) {
    if (attempt >= 5) return console.error(`[${businessId}] could not report "${event}" to the backend:`, err.message)
    setTimeout(() => reportSessionEvent(businessId, event, phoneNumber, isCurrent, attempt + 1), 60_000)
  }
}

export async function unlinkSession(businessId) {
  // A start still under way would leave a live session (and a scannable QR) behind the unlink
  // (QA run 7), so it finishes first and is then logged out like any other.
  // Bounded: a start that never settles mustn't hang the unlink (QA run 8).
  unlinkCount.set(businessId, (unlinkCount.get(businessId) ?? 0) + 1)
  const inFlight = starting.get(businessId)
  if (inFlight) await withTimeout(inFlight, 'waiting for a session start').catch(() => {})
  failures.delete(businessId)
  const session = sessions.get(businessId)
  if (session) {
    session.unlinking = true
    await session.sock.logout().catch(() => {})
    // A session still showing a QR has nothing to log out of; its socket is closed instead.
    try { session.sock.end(undefined) } catch {}
    sessions.delete(businessId)
  }
  await fs.rm(path.join(SESSIONS_DIR, businessId), { recursive: true, force: true }).catch(() => {})
}

export async function sendMessage(businessId, toPhone, message, media) {
  const session = sessions.get(businessId)
  if (!session || session.status !== 'connected') {
    throw new Error(`business ${businessId} has no connected WhatsApp session`)
  }
  const jid = `${toPhone.replace(/^\+/, '')}@s.whatsapp.net`
  await sendTracked(session.sock, jid, message, media)
}
