import './redactLogs.js'
import express from 'express'
import { timingSafeEqual } from 'crypto'
import { statusDto, startSession, restoreSessions, unlinkSession, sendMessage, isValidBusinessId } from './sessionManager.js'

const PORT = process.env.PORT || 3001
const BRIDGE_SECRET = process.env.BRIDGE_SECRET

if (!process.env.BACKEND_URL || !BRIDGE_SECRET) {
  console.error('BACKEND_URL and BRIDGE_SECRET are required')
  process.exit(1)
}

const app = express()
// Every request is server-to-server (the backend, over Railway's private network in production) --
// same shared-secret pattern as the backend's own CronSecret. Checked before the body is read
// (a stranger could make it parse 15MB bodies) and in constant time (QA run 6).
const secretBytes = Buffer.from(BRIDGE_SECRET)
app.use((req, res, next) => {
  const given = Buffer.from(String(req.headers['x-bridge-secret'] ?? ''))
  if (given.length !== secretBytes.length || !timingSafeEqual(given, secretBytes)) return res.status(401).json({ error: 'Unauthorized' })
  next()
})

// Chat attachments arrive inline (base64): up to 10MB files, a third larger once encoded.
app.use(express.json({ limit: '15mb' }))

// A business id names a session folder, so only plain ids get through (QA run 6).
app.param('businessId', (req, res, next, businessId) => {
  if (!isValidBusinessId(businessId)) return res.status(400).json({ error: 'Invalid business id' })
  next()
})

app.post('/sessions/:businessId/start', async (req, res) => {
  try {
    await startSession(req.params.businessId)
    res.json(statusDto(req.params.businessId))
  } catch (err) {
    console.error(`Failed to start session ${req.params.businessId}:`, err.message)
    res.status(500).json({ error: 'Failed to start session' })
  }
})

app.get('/sessions/:businessId/status', (req, res) => {
  res.json(statusDto(req.params.businessId))
})

app.delete('/sessions/:businessId', async (req, res) => {
  await unlinkSession(req.params.businessId)
  res.json({ ok: true })
})

app.post('/send', async (req, res) => {
  const { businessId, toPhone, message, media } = req.body
  if (!businessId || !toPhone || !message) return res.status(400).json({ error: 'businessId, toPhone, and message are required' })
  try {
    await sendMessage(businessId, toPhone, message, media)
    res.json({ ok: true })
  } catch (err) {
    res.status(409).json({ error: err.message })
  }
})

// Bad or oversized JSON came back as an HTML error page with a stack trace (QA run 7).
app.use((err, req, res, next) => {
  if (res.headersSent) return next(err)
  const status = err.status ?? err.statusCode ?? 500
  res.status(status).json({ error: err.type === 'entity.too.large' ? 'Request too large' : status < 500 ? 'Invalid request' : 'Internal error' })
})

app.listen(PORT, async () => {
  console.log(`whatsapp-bridge listening on :${PORT}`)
  await restoreSessions()
})
