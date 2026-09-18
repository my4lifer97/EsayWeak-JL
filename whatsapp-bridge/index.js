import express from 'express'
import { statusDto, startSession, restoreSessions, unlinkSession, sendMessage } from './sessionManager.js'

const PORT = process.env.PORT || 3001
const BRIDGE_SECRET = process.env.BRIDGE_SECRET

if (!process.env.BACKEND_URL || !BRIDGE_SECRET) {
  console.error('BACKEND_URL and BRIDGE_SECRET are required')
  process.exit(1)
}

const app = express()
app.use(express.json())

// Every request is server-to-server (the backend, over Railway's private network in production) --
// same shared-secret pattern as the backend's own CronSecret.
app.use((req, res, next) => {
  if (req.headers['x-bridge-secret'] !== BRIDGE_SECRET) return res.status(401).json({ error: 'Unauthorized' })
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
  const { businessId, toPhone, message } = req.body
  if (!businessId || !toPhone || !message) return res.status(400).json({ error: 'businessId, toPhone, and message are required' })
  try {
    await sendMessage(businessId, toPhone, message)
    res.json({ ok: true })
  } catch (err) {
    res.status(409).json({ error: err.message })
  }
})

app.listen(PORT, async () => {
  console.log(`whatsapp-bridge listening on :${PORT}`)
  await restoreSessions()
})
