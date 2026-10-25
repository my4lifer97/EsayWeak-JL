---
name: railway-and-billing
description: barber-saas production on Railway (services, URLs, bridge deploys, migration order, email-provider history) and Cardcom billing. Use before deploying, changing env/config, or touching billing.
---

<!-- Moved out of CLAUDE.md (2026-10-25) so it loads only when needed. -->

### Billing (Cardcom)
Billed via Cardcom (an Israeli payment gateway) using its "Low Profile" hosted-payment-page API (v11: `https://secure.cardcom.solutions/api/v11/...`), through `Services/ICardcomService`/`CardcomService.cs` (hand-rolled `HttpClient` wrapper -- Cardcom has no official .NET SDK, unlike Stripe.net which this replaced). `Cardcom:TerminalNumber`/`ApiName`/`ApiPassword` ship as empty strings in `appsettings.json` (no Cardcom account exists yet) — `BillingController` checks for `TerminalNumber`/`ApiName` and returns `503 { error: "Payments are not yet configured..." }` instead of attempting a call when they're blank. Once a real Cardcom account exists, set all three the same way as `Jwt:Secret`/`CronSecret`: `dotnet user-secrets` locally, environment variables (`Cardcom__TerminalNumber`, `Cardcom__ApiName`, `Cardcom__ApiPassword`) in production.

Unlike Stripe, Cardcom has no server-side "Subscription" object that auto-recurs — the initial payment (`Operation=ChargeAndCreateToken`) also mints a reusable charge token, which the app's own cron job (`GET /api/cron/charge-subscriptions`, see above) charges again every billing cycle.

- `POST /api/billing/checkout-session` (`BusinessOnly`) calls `CreateLowProfileAsync` (`ReturnValue = business.Id`, so the webhook can resolve the business directly instead of scanning by a stored customer id) and returns `{ url }` — the Cardcom-hosted payment page — for the frontend to redirect to. Redirects land back on `?billing=success`/`?billing=cancelled`.
- `POST /api/billing/webhook` (anonymous) — Cardcom's webhook has no HMAC signature like Stripe's, so the inbound POST is treated only as a trigger carrying a `LowProfileId`; the handler then calls `GetLowProfileResultAsync` server-to-server to fetch the **verified** result and only acts on that, never on fields taken directly from the webhook body. On a verified result with a `TokenNumber`, sets `Business.CardcomToken`/`CardcomNextChargeAt` (+1 month) and flips `SubscriptionStatus` to `ACTIVE`. `Business.CardcomLastLowProfileId` guards against a duplicate webhook redelivery re-processing the same `LowProfileId`.
- `SettingsPage.tsx`'s "Subscribe Now" button (shown whenever `subscriptionStatus !== 'ACTIVE'`) calls the checkout-session endpoint and redirects the browser to the returned Cardcom URL; on return with `?billing=success` it shows a brief banner and refetches settings (immediately and again ~3s later) since `ACTIVE` arrives asynchronously via the webhook, not synchronously on redirect.
- Several exact Cardcom JSON field/endpoint names (`GetLpResult`'s path, the recurring token-charge call's shape) are best-effort reconstructions flagged with comments in `CardcomService.cs` — Cardcom's docs are a JS-rendered SPA that couldn't be scraped when this was built. Verify against `https://secure.cardcom.solutions/Api/v11/Docs` or their Postman collection, and smoke-test against their public sandbox (Terminal `1000`, ApiName `demo`, card `4580000000000000`), before relying on this in production.

### Deployment (Railway)
Live at Railway project **accomplished-vitality**: backend `https://esayweak-jl-production.up.railway.app`,
frontend `https://frontend-production-5885.up.railway.app`, both deploying from `master` on push;
Postgres is internal-only (`railway connect Postgres --tunnel-only` for migrations/manual fixes).
**`whatsapp-bridge` is deployed to Railway** as its own service ("EsayWeak-JL - WhatsApp Bridge",
root dir `whatsapp-bridge`), with a Volume mounted at `/data` (`SESSIONS_DIR=/data/sessions`) so
sessions survive redeploys, and `BACKEND_URL` pointed at the backend's Railway **private** URL
(`http://esayweak-jl.railway.internal:8080`) — see `whatsapp-bridge/README.md`. Confirmed connected
in production as `+972556652545` (business `bbfc51d58c7d43f9adcdc0f34c8f5943`). Its logs show
recurring `connection closed ... (code 403)` churn that always self-recovers to `WhatsApp connected`
without a fresh QR scan — not yet fully explained (possibly normal Baileys/Railway network
behavior), worth another look if a business ever reports the bot going quiet.
**Production does not auto-migrate** — a migration-bearing commit must have its migration applied
via the tunnel *before* the push that deploys the new code, not after; this ordering has been
learned the hard way across five separate incidents (several full outages where EF selected a
column the DB didn't have yet). A `dotnet ef database update <MigrationName>` can be run against
just that one migration on the *old* code by temporarily checking out only the new migration
file(s) onto the current branch (no other model changes) before running it — see the Discovery
recovery in project history for a worked example. Vite bakes `VITE_API_URL` in at build time, so a
frontend env var change needs a fresh commit pushed (a Railway "Redeploy" alone won't rebuild).
Resend has no verified sending domain yet, so alone it can only deliver to the account owner's own
address. **Gmail SMTP was tried as a domain-free workaround and confirmed non-viable on Railway**
(2026-09-14): both ports 587 and 465 hit a `System.TimeoutException` connecting to
`smtp.gmail.com` — Railway blocks all outbound SMTP ports (25/465/587/2525) below the Pro plan
(confirmed via Railway's own support station; SMTP works fine in local dev, where this
restriction doesn't apply). **Brevo is the current fix** (`BrevoEmailSender`, takes precedence —
see Configuration above): its free tier sends to *any* recipient once a single sender address is
verified via a 6-digit code, no domain purchase needed, and being an HTTPS API it isn't affected
by the SMTP port block at all. Verifying a real Resend domain remains the longer-term option if
the product ever needs to send from a branded domain rather than a personal Gmail address.
