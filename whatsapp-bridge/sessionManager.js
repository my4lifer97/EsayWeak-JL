import { promises as fs } from 'fs'
import { randomBytes } from 'crypto'
import path from 'path'
import makeWASocket, { useMultiFileAuthState, fetchLatestBaileysVersion, DisconnectReason, generateMessageIDV2 } from '@whiskeysockets/baileys'
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

// The id is chosen and remembered before sending: Baileys echoes the message (messages.upsert)
// on process.nextTick, which can run before sendMessage's promise resolves.
async function sendTracked(sock, jid, text) {
  const messageId = generateMessageIDV2(sock.user?.id)
  sentIds.add(messageId)
  if (sentIds.size > MAX_SENT_IDS) sentIds.delete(sentIds.values().next().value)
  await sock.sendMessage(jid, { text }, { messageId })
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

export async function startSession(businessId) {
  const existing = sessions.get(businessId)
  if (existing && existing.status !== 'disconnected') return existing

  const authFolder = path.join(SESSIONS_DIR, businessId)
  const { state, saveCreds } = await useMultiFileAuthState(authFolder)
  const { version } = await fetchLatestBaileysVersion()

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
      lastQr = qr
      session.qr = await QRCode.toDataURL(withCurrentSecret(qr))
      session.status = 'qr'
    }

    if (connection === 'open') {
      failures.delete(businessId)
      session.status = 'connected'
      session.qr = null
      session.phoneNumber = sock.user?.id ? `+${sock.user.id.split(':')[0]}` : null
      console.log(`[${businessId}] WhatsApp connected as ${session.phoneNumber}`)
    }

    if (connection === 'close') {
      const statusCode = lastDisconnect?.error?.output?.statusCode
      const loggedOut = statusCode === DisconnectReason.loggedOut
      // Never linked (no `me` in the creds) and not the restart WhatsApp asks for right after a
      // scan: the QR expired unscanned ("QR refs attempts ended", 408). Reconnecting would start
      // a fresh QR every 3 s forever; stop instead, and the admin clicks Link again.
      const unlinkedQrExpired = !state.creds.me && statusCode !== DisconnectReason.restartRequired
      if (loggedOut || unlinkedQrExpired) {
        console.log(`[${businessId}] ${loggedOut ? 'logged out' : 'QR code not scanned in time'} -- clearing saved session`)
        sessions.delete(businessId)
        failures.delete(businessId)
        await fs.rm(authFolder, { recursive: true, force: true }).catch(() => {})
        // WhatsApp ended a linked session: tell the backend so the platform admin gets an email.
        // Not for an admin's own Unlink (unlinkSession), which logs out on purpose.
        if (loggedOut && !session.unlinking) reportLoggedOut(businessId, session.phoneNumber)
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
        setTimeout(() => { if (sessions.get(businessId) === session) startSession(businessId) }, delayMs)
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

function messageText(msg) {
  return msg.message?.conversation
    || msg.message?.extendedTextMessage?.text
    || msg.message?.imageMessage?.caption
    || ''
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
    body: JSON.stringify({ businessId, toPhone, message: text }),
  })
  if (!resp.ok) throw new Error(`backend responded ${resp.status}`)
}

async function handleInboundMessage(businessId, sock, msg) {
  if (!msg.message || msg.key.fromMe) return
  if (!msg.key.remoteJid || msg.key.remoteJid.endsWith('@g.us')) return // ignore group messages -- chatbot is 1:1 only

  const text = messageText(msg)
  if (!text.trim()) return

  const jid = await resolvePhoneJid(sock, msg)
  if (!jid) {
    console.error(`[${businessId}] could not resolve a real phone number for LID ${msg.key.remoteJid} -- dropping message rather than saving a bogus phone`)
    return
  }

  const fromPhone = `+${jid.split('@')[0]}`
  const profileName = msg.pushName || null

  const resp = await fetch(`${BACKEND_URL}/api/whatsapp/bridge/inbound`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Bridge-Secret': BRIDGE_SECRET },
    body: JSON.stringify({ businessId, fromPhone, profileName, message: text }),
  })
  if (!resp.ok) throw new Error(`backend responded ${resp.status}`)
  const { reply } = await resp.json()
  if (reply) await sendTracked(sock, jid, reply)
}

// Best-effort, a few tries a minute apart in case the backend is mid-deploy.
async function reportLoggedOut(businessId, phoneNumber, attempt = 1) {
  try {
    const resp = await fetch(`${BACKEND_URL}/api/whatsapp/bridge/session-event`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'X-Bridge-Secret': BRIDGE_SECRET },
      body: JSON.stringify({ businessId, event: 'loggedOut', phoneNumber }),
    })
    if (resp.ok || resp.status === 404) return
    throw new Error(`backend responded ${resp.status}`)
  } catch (err) {
    if (attempt >= 5) return console.error(`[${businessId}] could not report the logout to the backend:`, err.message)
    setTimeout(() => reportLoggedOut(businessId, phoneNumber, attempt + 1), 60_000)
  }
}

export async function unlinkSession(businessId) {
  failures.delete(businessId)
  const session = sessions.get(businessId)
  if (session) {
    session.unlinking = true
    await session.sock.logout().catch(() => {})
    sessions.delete(businessId)
  }
  await fs.rm(path.join(SESSIONS_DIR, businessId), { recursive: true, force: true }).catch(() => {})
}

export async function sendMessage(businessId, toPhone, message) {
  const session = sessions.get(businessId)
  if (!session || session.status !== 'connected') {
    throw new Error(`business ${businessId} has no connected WhatsApp session`)
  }
  const jid = `${toPhone.replace(/^\+/, '')}@s.whatsapp.net`
  await sendTracked(session.sock, jid, message)
}
