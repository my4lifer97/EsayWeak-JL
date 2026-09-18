# whatsapp-bridge

Self-hosted WhatsApp transport for barber-saas's chatbot, via [Baileys](https://github.com/WhiskeySockets/Baileys)
(drives a regular linked WhatsApp number, like WhatsApp Web/multi-device — no Meta Business
verification). All booking/chatbot logic stays in the C# backend; this service is a thin transport
adapter: it holds one Baileys session per business, forwards inbound messages to the backend, and
sends back whatever reply the backend returns.

**Known tradeoff**: this uses WhatsApp outside its official Business API and violates WhatsApp's
Terms of Service — a linked number can be banned by Meta's automated detection with no appeal. See
`CLAUDE.md`'s WhatsApp section for the full context on why this path was chosen.

## Running locally

```powershell
$env:BACKEND_URL = "http://localhost:5280"
$env:BRIDGE_SECRET = "<any value, must match the backend's WhatsAppBridge:Secret user-secret>"
npm install
npm start
```

Then, from the backend, hit `POST /api/platform-admin/businesses/{id}/whatsapp/link` (or use the
platform-admin UI's "Link WhatsApp" button) to start a session and get a QR code back.

## Environment variables

| Variable | Required | Purpose |
|---|---|---|
| `BACKEND_URL` | yes | Base URL of the barber-saas backend (its Railway *private* URL in production, e.g. `http://esayweak-jl-backend.railway.internal`) — inbound messages are POSTed to `{BACKEND_URL}/api/whatsapp/bridge/inbound` |
| `BRIDGE_SECRET` | yes | Shared secret checked on every request in both directions — must match the backend's `WhatsAppBridge:Secret` |
| `PORT` | no (default `3001`) | HTTP port this service listens on |
| `SESSIONS_DIR` | no (default `./data/sessions`) | Where Baileys auth state is persisted, one folder per business — **must be a persistent volume in production**, or every redeploy forces every business to re-scan their QR code |

## Deployment (Railway)

Deploy as its own Railway service (root dir `whatsapp-bridge`) with a **Volume** mounted where
`SESSIONS_DIR` points, and `BACKEND_URL` pointed at the backend's private (`*.railway.internal`)
URL so inbound-message traffic never leaves Railway's network.
