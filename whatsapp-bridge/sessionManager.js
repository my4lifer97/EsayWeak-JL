import { promises as fs } from 'fs'
import path from 'path'
import makeWASocket, { useMultiFileAuthState, fetchLatestBaileysVersion, DisconnectReason } from '@whiskeysockets/baileys'
import { pino } from 'pino'
import QRCode from 'qrcode'

const SESSIONS_DIR = process.env.SESSIONS_DIR || './data/sessions'
const BACKEND_URL = process.env.BACKEND_URL
const BRIDGE_SECRET = process.env.BRIDGE_SECRET

// businessId -> { sock, status: 'connecting'|'qr'|'connected'|'disconnected', qr, phoneNumber }
// One process, many sockets -- Baileys sessions are just websocket connections, not separate OS
// processes, so this comfortably holds every business's linked number.
const sessions = new Map()

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
    console.log(`Restoring WhatsApp session for business ${businessId}`)
    await startSession(businessId).catch((err) => console.error(`Failed to restore session ${businessId}:`, err.message))
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

  sock.ev.on('connection.update', async (update) => {
    const { connection, lastDisconnect, qr } = update

    if (qr) {
      session.qr = await QRCode.toDataURL(qr)
      session.status = 'qr'
    }

    if (connection === 'open') {
      session.status = 'connected'
      session.qr = null
      session.phoneNumber = sock.user?.id ? `+${sock.user.id.split(':')[0]}` : null
      console.log(`[${businessId}] WhatsApp connected as ${session.phoneNumber}`)
    }

    if (connection === 'close') {
      const statusCode = lastDisconnect?.error?.output?.statusCode
      const loggedOut = statusCode === DisconnectReason.loggedOut
      if (loggedOut) {
        console.log(`[${businessId}] logged out -- clearing saved session`)
        sessions.delete(businessId)
        await fs.rm(authFolder, { recursive: true, force: true }).catch(() => {})
      } else {
        session.status = 'disconnected'
        console.log(`[${businessId}] connection closed, reconnecting in 3s (code ${statusCode})`)
        setTimeout(() => startSession(businessId), 3000)
      }
    }
  })

  sock.ev.on('messages.upsert', async ({ messages, type }) => {
    if (type !== 'notify') return
    for (const msg of messages) {
      await handleInboundMessage(businessId, sock, msg).catch((err) =>
        console.error(`[${businessId}] failed to process inbound message:`, err.message))
    }
  })

  return session
}

async function handleInboundMessage(businessId, sock, msg) {
  if (!msg.message || msg.key.fromMe) return
  const jid = msg.key.remoteJid
  if (!jid || jid.endsWith('@g.us')) return // ignore group messages -- chatbot is 1:1 only

  const text = msg.message.conversation
    || msg.message.extendedTextMessage?.text
    || msg.message.imageMessage?.caption
    || ''
  if (!text.trim()) return

  const fromPhone = `+${jid.split('@')[0]}`
  const profileName = msg.pushName || null

  const resp = await fetch(`${BACKEND_URL}/api/whatsapp/bridge/inbound`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Bridge-Secret': BRIDGE_SECRET },
    body: JSON.stringify({ businessId, fromPhone, profileName, message: text }),
  })
  if (!resp.ok) throw new Error(`backend responded ${resp.status}`)
  const { reply } = await resp.json()
  if (reply) await sock.sendMessage(jid, { text: reply })
}

export async function unlinkSession(businessId) {
  const session = sessions.get(businessId)
  if (session) {
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
  await session.sock.sendMessage(jid, { text: message })
}
