# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Stack

**Backend**: `backend/` — ASP.NET Core 9 Web API (C#) + Entity Framework Core 9 + PostgreSQL  
**Frontend**: `frontend/` — Vite 5 + React 19 + TypeScript SPA

Despite the repo/namespace still being called `BarberSaas`, the product has been generalized to a
multi-vertical platform — see [Multi-vertical: business types & models](#multi-vertical-business-types--models).
"Barber" now survives only as one `BusinessTypeDefinition` row and in a few historical
identifiers (`BarberSaas.Api.csproj`, old migration filenames); the domain model itself is
`Business`/`Item`, not `Barber`/`Service`.

## CI

`.github/workflows/ci.yml` runs on every push to `master` and every PR: `dotnet build`/`test`
for the backend (SQLite in-memory, no Postgres service needed — see backend test docs below)
and `tsc`/`vitest`/`vite build` for the frontend, on GitHub-hosted runners (Node 22, .NET 9).
CI-only for now — no deploy step; deploys to Railway are triggered separately by pushing to
`master` (see [Deployment (Railway)](#deployment-railway)). Playwright E2E is intentionally not
in CI: it needs a live backend against a real Postgres instance plus the Vite dev server, which
is meaningfully more orchestration than the build/test jobs above.

## Node.js Environment

Two Node.js installs are on this machine:

- **Cursor Node v24** (has `node.exe`, no npm) — path:  
  `C:\Users\Jamel\AppData\Roaming\Cursor\User\globalStorage\anysphere.cursor-agent-worker\agent-cli\.local\share\cursor-agent\versions\2026.06.24-00-45-58-9f61de7`
- **VS Node v20.13.1** (has `npm.cmd` / `npx.cmd`) — path:  
  `C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Microsoft\VisualStudio\NodeJs`

Use VS npm for `npm install` and `npx.cmd vite`. Vite 5 + Tailwind 3 are pinned because VS Node v20.13.1 is below the v20.19 minimum required by Vite 9.

```powershell
$vsNpmDir = "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Microsoft\VisualStudio\NodeJs"
$env:PATH = "$vsNpmDir;$env:PATH"
```

## Commands

### Backend (C#)
```powershell
Set-Location "C:\Users\Jamel\Desktop\EsayWeek_JL\barber-saas\backend"
dotnet run                           # Start API at http://localhost:5280
dotnet build                         # Build only
dotnet ef migrations add <Name>      # Create EF migration
dotnet ef database update            # Apply migrations
```

### Backend tests (`BarberSaas.Api.Tests/`)
```powershell
Set-Location "C:\Users\Jamel\Desktop\EsayWeek_JL\barber-saas\BarberSaas.Api.Tests"
dotnet test
```
xUnit project, sibling to `backend/` (not nested inside it — an SDK-style project's default
glob would otherwise pull the test `.cs` files into the API's own compilation). Integration
tests use `WebApplicationFactory<Program>` (`TestWebApplicationFactory.cs`) against a SQLite
in-memory database (not the EF InMemory provider — `CustomerAuthController` uses
`ExecuteUpdateAsync`, a relational-only operation InMemory can't execute); `AvailabilityServiceTests`
unit-tests `Services/AvailabilityService.cs` directly the same way. Test config (Jwt secret,
CronSecret, etc.) is injected via environment variables in the factory's constructor, not
`ConfigureAppConfiguration` — the latter applies too late for minimal-API top-level statements
that read `IConfiguration` before `WebApplicationFactory`'s hook runs, which previously caused
`JwtService` to sign tokens with a different secret than the bearer middleware validated with.

### Frontend (React)
```powershell
# Set VS npm in PATH first (see above)
Set-Location "C:\Users\Jamel\Desktop\EsayWeek_JL\barber-saas\frontend"
npx.cmd vite                         # Dev server at http://localhost:5173
npx.cmd vite build                   # Production build
npx.cmd tsc -b                       # Type check -- same as the Railway build; plain `tsc --noEmit` skips src/ (project references) and misses errors
```

Both must run simultaneously. Vite dev server proxies `/api/*` to `http://localhost:5280`.

### Frontend tests
```powershell
npx.cmd vitest run                   # Unit/component tests (jsdom + React Testing Library)
npx.cmd playwright test              # E2E — needs both dev servers already running (real API, not mocked)
```
- **Unit tests** (`*.test.ts(x)` next to the file they cover): `vitest` + `@testing-library/react`.
  `vitest` is pinned to `^3.x` — `vitest@4` requires Vite `^6/7/8`, incompatible with the
  Vite 5 pin above (Node 20.13.1); `jsdom` is pinned to `^25.x` — `jsdom@29` pulls an ESM-only
  dependency (`@exodus/bytes`) that breaks under `require()` in this toolchain.
  `ProtectedRoute`/`CustomerProtectedRoute` live in `src/components/` (not inlined in `App.tsx`)
  specifically so they're testable in isolation with a `MemoryRouter`.
- **E2E** (`e2e/*.spec.ts`): `@playwright/test`, Chromium only. Each test seeds its own business via
  direct API calls (register/login/create-item) rather than relying on existing data, so
  it's safe to run against the same DB repeatedly. `vite.config.ts`'s `test.exclude` keeps
  vitest from also picking up these `.spec.ts` files (both tools default to the same glob).
  `barber-login-gate.spec.ts` additionally needs local dev's backend `Twilio:AuthToken` user-secret
  set to `"test-auth-token"` (or export `TWILIO_AUTH_TOKEN` to match whatever it's actually set to)
  — it signs a real webhook request against that value, same as production's signature check.

## Architecture

Multi-tenant SaaS. Each **business** is a tenant identified by a URL slug (originally single-vertical
"barber shop" software; see [Multi-vertical](#multi-vertical-business-types--models) for how that
generalized). The internal C# type/table name is still `Item` for what a customer books ("Service"
in older docs/commits) — bookable and showcase-only items are the same table, distinguished by
`Item.IsBookable`.

### Feature reference (on-demand skills)
Detailed per-area notes live in `.claude/skills/*/SKILL.md` (moved out of this file 2026-10-25 so
they load only when needed). Read the matching one before changing that area:
- `.claude/skills/api-routes/SKILL.md` -- every endpoint and its rules
- `.claude/skills/bookings-and-scheduling/SKILL.md` -- availability, overlap lock, limits, waitlist, cancel approval, recurring, status, photos
- `.claude/skills/platform-admin-and-onboarding/SKILL.md` -- platform admin, owner emails, onboarding, business types, reviews, directory
- `.claude/skills/whatsapp-chatbot/SKILL.md` -- bridge, chatbot, Inquiry mode, Chats page, customer login (WhatsApp + OTP)
- `.claude/skills/railway-and-billing/SKILL.md` -- production deploys and Cardcom

Rules that apply everywhere (details in those files):
- **"Now" is `Services/AppClock.Now`** (Israel time) -- never `DateTime.Now`/`DateTime.UtcNow` for wall-clock logic; Railway runs in UTC.
- **Production does not auto-migrate**: apply a migration to the prod DB *before* pushing the code that needs it (several outages came from the reverse).
- Backend and frontend deploy from `master` on push; `whatsapp-bridge` does not -- deploy it with `railway up --detach -s "EsayWeak-JL - WhatsApp Bridge"` from `barber-saas/` (no `--path-as-root`), after the backend it depends on is live.

### Auth
JWT Bearer token stored in `localStorage`. `api.ts` adds it automatically via request interceptor. 401 responses redirect to `/admin/login` — **except** a 401 from `/auth/login` itself (wrong password), which must NOT redirect or it wipes `LoginPage`'s own error message via a full page reload before React can render it. Admin routes are wrapped in `ProtectedRoute` (`frontend/src/components/ProtectedRoute.tsx`) which checks `useAuth().isAuthenticated`, and further gated by `/admin/set-password` while `mustChangePassword` is set (see [Business owner onboarding](#business-owner-onboarding-self-service--admin-approved)). The platform-admin panel is a fully separate auth stack (`platformAdminAuth.tsx`, `PlatformAdminProtectedRoute`) — its JWT is never sent on business/customer requests or vice versa.

**Session rules (2026-10-11):**
- `BusinessOnly` accepts only business JWTs, which are the only ones without a `type` claim. It used to only exclude customers, so a platform-admin token worked on owner routes.
- **Disabled businesses** (`Business.IsDisabled`) are blocked at login, reset-password, verify-email, *and* on every `BusinessOnly` action of an already-issued token (`Filters/BlockDisabledBusinessFilter.cs`, 403 `{ accountDisabled: true }` → `api.ts` signs out to `/admin/login?disabled=1`). Forgot-password answers them with the same silent 200 as an unknown email. Platform-admin impersonation tokens are let through so support can still look inside.
- **Who takes bookings** (`Services/BookingRules` over `Business.AcceptsBookings` / the query form `AcceptsBookingsAt`, 2026-10-21): not disabled, subscription not `EXPIRED`, and not a `TRIAL` past `TrialEndsAt` -- the trial part only once billing is configured (`Cardcom:TerminalNumber` + `ApiName`): until then nobody can subscribe, so trials never end (owner's decision). A business failing it is left out of `/api/businesses/search` and `/cities`, `GET /api/{slug}/info` returns `acceptsBookings: false` (the storefront hides "Book" and `/:slug/book` shows `bookingUnavailable` instead of the wizard), and booking, both customer reschedules, waitlist joins and the chatbot's links answer 403 "This business isn't taking online bookings right now". Customers can still view and cancel what they already booked.
- **A password change ends other logins** (2026-10-12). Business JWTs carry a `pwd` claim, `JwtService.PasswordStamp(PasswordHash)`, a short SHA-256 fingerprint of the bcrypt hash. The same filter returns 401 when it no longer matches, which `api.ts` turns into a sign-out. A token with no stamp (issued before this existed) is rejected too. Every path that sets a new hash ends old sessions: reset, change and the platform admin's reset-password. Change-password and reset-password return a fresh token so that device stays signed in. Impersonation tokens have no stamp and skip the check.
- **Owner emails are case-insensitive** (`Services/EmailAddress.Normalize`: trim + lowercase). New rows are stored normalized. Lookups compare `Email.ToLower()` so older mixed-case rows still match. That covers register/login/verify/forgot/reset, business-owner requests and approval. Usernames match case-insensitively at login too.
- All three auth providers call `queryClient.clear()` on login and logout, so a shared device never shows (or lets Settings save) the previous session's data. `SettingsPage`/`ChatbotSettingsPage` also refill their form when the business slug changes.
- `customerApi.ts`/`platformAdminApi.ts` dispatch `CUSTOMER_SESSION_EXPIRED`/`PLATFORM_ADMIN_SESSION_EXPIRED` window events on a 401 (not from their own login endpoints); the providers listen and drop to signed-out, instead of leaving "Sign Out" showing over empty pages.
- **Impersonation** ("Log in as") stores the target's token in the normal owner/customer slots plus an `impersonation` marker; `lib/impersonation.ts` owns it. Platform-admin logout or expiry calls `endImpersonation()` (removes that login, full reload), and an owner/customer logout or 401 calls `clearImpersonationMarker(type)` so the yellow banner never outlives the impersonated session.
- **One owner login per tab** (2026-10-22, QA run 4): `localStorage` is shared by every tab, so a "Log in as" or another login in a second tab used to switch an open owner tab's lists and saves to that business. `api.ts` keeps the token the tab signed in with (`setOwnerTabToken`, set by `AuthProvider` on login/logout) and sends that one; when the stored token changes in another tab (`storage` event), `AuthProvider` covers the page with `components/admin/SessionChangedNotice` (Reload), and an error from the old token doesn't sign the other tab out. The notice clears if the other tab puts the same login back.
- **Login throttling** (`Services/LoginThrottle.cs`, in memory): owner and platform-admin password logins return 429 + `Retry-After` after too many failures in 15 min: 5 per account per IP (so a stranger can't lock the real owner out), 20 per account across IPs, 50 per IP. A success clears that account+IP count. The client IP comes from `app.UseForwardedHeaders()` reading **`X-Real-IP`**. Railway overwrites it, so clients can't spoof it. Don't use `X-Forwarded-For`'s last entry: on Railway it's `<client>, <edge>`, and that last entry is the edge. Before that, `RemoteIpAddress`, and so every activity-log IP, was the proxy's.

**Customer routes**: `/:slug/book` and `/account/bookings` are wrapped in `CustomerProtectedRoute` (`frontend/src/components/CustomerProtectedRoute.tsx`) — an anonymous visitor to a guarded route sees an inline "Please sign in to continue" notice with a link into `/login?next=<path + query>` (the query keeps a waitlist link's slot) (phone+OTP, see [Customer login via phone+OTP](#customer-login-via-phoneotp)), not a hard redirect, since there's no single canonical sign-in page the way business/platform-admin auth has. As of the Discovery phase, **`/:slug` and `/browse` are public** (read-only storefront + directory) — moved out of the guard so a logged-out visitor who finds a business can view it; booking still needs a customer session (from either login path). `/:slug/appointments/:id` (the magic-link view) and `/:slug/w/:token` (the WhatsApp landing point, which establishes the session itself) are also outside the guard. `CustomerAccountNav` (shown on customer pages) links to `/login` when logged out, same as the guard's fallback.

**Following** has no dedicated page/route (`/account/following` was removed) — `BrowseBusinessesPage` (`/browse`) fetches `GET /api/businesses/followed` itself and renders a "Businesses You Follow" list below the directory results (authenticated only). A customer is auto-followed to a business the moment they book an appointment (`BookingController.BookAppointment`, which requires a customer session), not just via an explicit Follow click. The Follow button on `/:slug` and on browse cards sends an anonymous visitor to `/login?next=...`.

### i18n (Translations)
- **Frontend**: `frontend/src/lib/i18n.ts` — typed `const` object with EN/AR/HE strings (every new key must be added to all three).  
  Use `t(lang, 'key')` for UI strings and `itemName(item, lang)` for multilingual item names.  
  **Customer-facing pages** (browse/account/*, a business's public page, the booking wizard) use the
  customer's own language preference — `useCustomerAuth().language`/`setLang()`, stored under
  `localStorage['customerLang']`, defaulting to **Hebrew** when unset. This is independent of, and
  overrides, that specific business's own configured `language`/`isRTL` (their storefront
  setting) — a customer who picks English sees English everywhere, even on a Hebrew-configured
  business's page. RTL is derived from the customer's chosen language (`AR`/`HE` → `rtl`), not the
  business's `isRTL` flag. `<LanguageSwitcher />` (`frontend/src/components/customer/`) exposes the
  picker; it's on `CustomerAccountNav`, `BusinessPage`, and `BookingWizard`.
  **Admin/business dashboard pages** are unaffected — they still use `useAuth().language`, set from the
  business's own `Settings > Language` field, unrelated to any customer's choice.
  The signed-out owner pages (login, register, forgot/set password, `/request-business-account`) use the
  same `useAuth().language` (`localStorage['lang']`, default English) through `components/admin/OwnerAuthShell.tsx`,
  which adds a HE/EN/AR picker (`components/LanguagePicker.tsx`, shared with the customer `LanguageSwitcher`)
  and the `dir`; signing in replaces it with the business's setting. Placeholders like `{email}` are filled
  with `.replace()`, or `components/WithEmail.tsx` when the value needs a `<bdi>`. The platform-admin panel is English only on purpose.
- **Server error messages**: controllers write English `{ error = "..." }`. `api.ts`/`customerApi.ts` send
  `X-Lang` (owner `lang` / customer `customerLang`; `AppointmentPage` passes the business's language), and the
  global `Filters/TranslateErrorFilter.cs` swaps the text via `Services/ApiErrorTranslations.cs` for AR/HE
  (exact strings, plus regex patterns for messages with a number/name inside). A message missing there stays
  English, so add its AR/HE line when adding a user-facing error.
  Every translated page sets `dir` on its root (`AR`/`HE` → `rtl`). Inside rtl text, wrap time
  ranges, phone numbers and counts in `<bdi dir="ltr">` (or a plain `<bdi>` for a generic
  label/value row): unwrapped, "09:00 – 10:00" displays as "10:00 – 09:00" and "+972…" puts the `+`
  at the end. Phone/OTP inputs get `dir="ltr"`.
- **Backend**: `backend/Services/I18nService.cs` — static `T(lang, key, args)` for WhatsApp/reminder messages.

### Code splitting
Owner dashboard pages (inside `AdminLayout`) and all platform-admin pages are `lazyPage(() => import(...))`
in `App.tsx` (`lib/lazyPage.ts`: `React.lazy` that reloads the tab once if a chunk from an older deploy is
gone). Customer pages and the owner sign-in pages stay in the main bundle. `vite.config.ts` puts the
libraries in a separate `vendor` chunk so it stays cached across deploys. `Suspense` boundaries: the root
route's `Outlet` and `AdminLayout`'s.

### Back navigation (customer pages)
`frontend/src/components/BackButton.tsx` — browser-history back (`navigate(-1)`), not a fixed
route, so it works regardless of how the customer arrived. Used on every customer-facing page
(BusinessPage, BookPage/BookingWizard step 1, MyBookingsPage, BrowseBusinessesPage,
AppointmentPage). BookingWizard steps 2-4 keep their own in-wizard step-back button instead
(moving between wizard steps, not pages).

### Input validation (global)
`Filters/ValidateInputFilter.cs` (global, 2026-10-13) answers 400 before an action runs when:
- a `date`/`startDate`/`endDate` query argument, or a `Date`/`StartDate`/`EndDate` string property on a request DTO, isn't an exact `yyyy-MM-dd` (years 2000–2100). Endpoints still call `DateTime.Parse`, which turned "2026-02-30" into a 500 before this.
- a `Notes` property is over 2000 characters, or any `*Name` property is over 200.

It checks only action arguments whose type is in this assembly (our DTOs), plus plain string arguments. `AvailabilityService` offers no slots more than `MaxDaysAhead` (365) days out. Booking requires a non-blank `customerName`. Settings rejects a blank business name. Items go through `AdminController.ItemInputError` on create *and* edit: names, a duration of at most 24 h, and a price between 0 and 1,000,000. `SlugValidator` reserves every top-level frontend route, including `platform-admin`, `request-business-account` and `wa-link`, plus `admin-platform`, which `App.tsx` redirects to `/platform-admin` (the swapped spelling used to load as a storefront and 404). Keep it in sync with `App.tsx`.

### Background sends (NotificationQueue)
Best-effort WhatsApp/email sends no longer run inside the request (QA run 2: an owner cancel that
offered the slot to the waitlist took ~8s waiting on the bridge). An action adds them to the
scoped `NotificationOutbox` (`AddWhatsApp`/`AddOwnerEmail`); the global `DispatchNotificationsFilter`
hands them to the singleton `NotificationQueue` after the action ran (so after its SaveChanges), or
drops them if it threw; `NotificationWorker` (hosted) runs them, up to 4 at once, each in its own DI
scope. A job must not capture request-scoped services, only data. Queued so far: booking
confirmations (customer and owner bookings), the owner's cancellation-approval WhatsApp/email,
chatbot inquiry alerts, and the first waitlist message after a cancel/reschedule (the entry is
marked `NOTIFIED` in the request; a failed send puts it back to `WAITING` for `WaitlistQueueWorker`).
Still in the request: login/verification codes, platform-admin emails (their result is shown),
cron reminders and the waitlist worker's own sends. Jobs queued at shutdown are lost.
Tests run jobs inline (`Notifications__Inline=true`, set by `TestWebApplicationFactory`, whose
`inlineNotifications: false` runs the real queue -- `BackgroundNotificationTests`, non-parallel).

**Customer notices for owner actions** (`Services/CustomerNotifier.cs`, queued the same way): the
owner cancelling (`whatsapp.ownerCancelled`, link to the storefront), rescheduling
(`whatsapp.ownerRescheduled`, old and new time + the magic link), keeping a cancellation request
(`whatsapp.cancellationDeclined`) or cancelling/replacing one (`whatsapp.cancellationApproved`)
messages the customer; replacing also confirms the booking to the new customer. A cancel of an
appointment that's already over sends nothing. Each request takes `notifyCustomer` (default true;
`keep` takes it as a query parameter), the "Let the customer know on WhatsApp" checkbox in
`CancelOptionsModal`/`RescheduleModal`, shown only when the business has a linked number. Language:
the customer's `LastMessageLanguage` (account matched by phone too), else `ChatbotDefaultLanguage ?? Language`.
Owner-booked confirmations go through it as well. The customer's own booking confirmation (after the
pending chat link's language) and the cron reminders use the same rule (QA run 4; they used `Language`). Deleting a recurring series sends one
`whatsapp.ownerCancelledSeries` listing every cancelled upcoming date (`DELETE /api/admin/recurring/{id}?notifyCustomer=`,
the checkbox in the delete confirm via `ConfirmDialog`'s `checkbox` option).

### No native dialogs on customer pages
Customers mostly arrive through WhatsApp links, i.e. inside WhatsApp's in-app browser, which
silently blocks `window.confirm()`/`alert()` (confirm returns false, so the action just does
nothing). Customer-facing confirmations use `components/customer/InlineConfirm.tsx` (on-page
Yes/No) instead — cancelling an appointment (`AppointmentCard`, `AppointmentPage`) and deleting a
review (`BusinessReviews`) — and surface the server's error text on failure rather than failing
silently. Admin pages use the same idea as a dialog: `components/ConfirmDialog.tsx`'s
`useConfirmDialog(lang)` returns an async `ask(message, { yesLabel, noLabel })` plus the element to
render — no `window.confirm()` anywhere in the frontend.

**Unsaved edits**: `components/admin/useUnsavedChangesGuard(dirty, lang)` asks before leaving a page
with unsaved changes (in-app navigation via `useBlocker`, reload/close via `beforeunload`; signing out
is never blocked). Used by Settings, Chatbot settings and the service form (which also asks before
closing its modal). `useBlocker` needs a data router, so `App.tsx` uses `createBrowserRouter`, not
`<BrowserRouter>`, and component tests of these pages render through `createMemoryRouter` + `RouterProvider`.

### Database
EF Core + Npgsql. Dev DB: `barbersaas_dev` (appsettings.Development.json). Prod DB: `barbersaas` (appsettings.json). Auto-migrates in Development on startup.  
All times stored as `"HH:MM"` strings — zero-padded so string comparison is safe.  
Migrations run roughly: `InitialCreate` ... `AddPlatformAdminAndActivityLog` ... `AddWhatsAppBookingFlow`
... `AddBusinessTypeAndModel` → `RenameBarberToBusiness` → `GeneralizeServiceToItem` (the
multi-vertical rename) → `AddBusinessOwnerRequests` → `AddReviews` (adds `Reviews` +
`Businesses.RatingCount`/`RatingAverage`) → `AddBusinessDiscoveryFields` (adds
`Businesses.City`/`AddressLine`/`MapUrl`/`IsListed` + indexes) → `AddCustomerOtp` (adds the
`CustomerOtps` table for phone+OTP login) → … → `AddConversationShownItemIds` (2026-10-13) → `AddAppointmentIsWaitlistCarrier` (2026-10-18) → `AddChatMessages` → `AddChatAttachments` → `AddQuickReplies` → `AddAppointmentAttendance` (2026-10-25), the latest as of this writing. Production does **not** auto-migrate (`db.Database.Migrate()` runs only when
`IsDevelopment()`); a migration-bearing deploy must apply it via the Railway Postgres tunnel
**before** the code push, not after (see [Deployment (Railway)](#deployment-railway)) — skipping
that ordering has caused multiple prod outages.

### Configuration (`backend/appsettings.json`)
```
ConnectionStrings:Default   PostgreSQL connection string (prod: barbersaas)
Jwt:Issuer                  barbersaas-api
Jwt:Audience                barbersaas-frontend
AppUrl                      Public frontend URL (used in WhatsApp message links, including booking-link tokens)
AllowedOrigin               CORS allowed origin (frontend URL)
RecurringGeneration:HorizonWeeks   How many weeks ahead RecurringAppointmentService keeps generated (optional, defaults to 8)
BackendUrl                  Public backend URL (used to build Cardcom's WebHookUrl callback -- must be a URL Cardcom's servers can reach, unlike AppUrl which points at the frontend)
Cardcom:TerminalNumber      Cardcom terminal number -- billing is disabled (503) until this is set
Cardcom:ApiName             Cardcom API name (sent on LowProfile/Create and GetLpResult)
Cardcom:ApiPassword         Cardcom API password (sent on the recurring token-charge call only, not on LowProfile/Create)
Cardcom:MonthlyAmount       Subscription amount in ILS, as a string (default "120")
Platform:AdminNotificationEmail   Where BusinessOwnerRequestsController emails a best-effort notification on new signup requests (optional -- silently skipped if unset)
WhatsAppBridge:Url          Base URL of the whatsapp-bridge service (its Railway private URL in production, e.g. http://localhost:3001 locally)
WhatsAppBridge:Secret       Shared secret between this backend and whatsapp-bridge (checked on both ends, same pattern as CronSecret)
OpenAI:ApiKey               Enables the optional LLM-driven WhatsApp chatbot layer when set (see the OpenAI layer section above) -- rule-based flow used otherwise
OpenAI:Model                Chat model to use (optional, defaults to "gpt-4o-mini" -- check current OpenAI model availability/pricing before relying on this default)
Gmail:ClientId               OAuth2 client ID for the platform-admin owner-email composer's Gmail sender (GmailApiEmailSender) -- a "Web application"-type OAuth client (Desktop-app type won't let the OAuth Playground register a redirect URI for the one-time token step)
Gmail:ClientSecret           OAuth2 client secret paired with Gmail:ClientId
Gmail:RefreshToken           Long-lived refresh token for the Gmail account owner-emails are sent from, obtained once via https://developers.google.com/oauthplayground (gear icon -> "Use your own OAuth credentials" -> scope gmail.send -> Authorize -> Exchange authorization code for tokens); the app must be in "Testing" publishing status with that Gmail account added as a test user, or authorization is rejected
Gmail:FromEmail               The Gmail address the refresh token was issued for (the actual From on sent mail)
```

`Jwt:Secret`, `CronSecret`, `Twilio:AccountSid`/`Twilio:AuthToken`, `Twilio:FromNumber`, `WhatsAppBridge:Secret`, `OpenAI:ApiKey`, and `Gmail:ClientId`/`ClientSecret`/`RefreshToken`/`FromEmail` are **not** in `appsettings.json` — there's no default, so the app fails fast (or, for the Twilio/bridge/Gmail values, simply can't validate/send) if they're missing rather than silently falling back to a guessable value. `Twilio:FromNumber` is a platform-level SMS-capable Twilio number for the phone+OTP customer login code (`TwilioOtpSender`) — separate from WhatsApp entirely. `Twilio:AccountSid`/`Twilio:AuthToken` back the dormant legacy WhatsApp path only (see [WhatsApp chatbot: self-hosted via Baileys](#whatsapp-chatbot-self-hosted-via-baileys-whatsapp-bridge)) — nothing in production currently uses them for WhatsApp.
- **Local dev**: stored in the `dotnet user-secrets` store for `backend/BarberSaas.Api.csproj` (`UserSecretsId` in the `.csproj`, values live outside the repo at `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`). `dotnet run` loads them automatically in Development. **The test suite forces dummy `Gmail:*` env vars in `TestWebApplicationFactory`'s constructor specifically to shadow these** (same reasoning as its `Resend:ApiKey` override) — a developer's real local Gmail secrets would otherwise leak into test runs via the shared `UserSecretsId`.
- **Production**: supply via environment variables (`Jwt__Secret`, `CronSecret`, `Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__FromNumber`, `WhatsAppBridge__Url`, `WhatsAppBridge__Secret`, `OpenAI__ApiKey`, `OpenAI__Model`, `Gmail__ClientId`, `Gmail__ClientSecret`, `Gmail__RefreshToken`, `Gmail__FromEmail`) or `appsettings.Production.json` (gitignored) — never commit real values.
- Rotating `Jwt:Secret`/`CronSecret` invalidates all existing JWTs/cron callers signed with the old value — expected, not a bug.

**Email delivery** (`Services/IEmailSender.cs` + implementations) — precedence decided once at
startup in `Program.cs`, only one sender is ever active: **Brevo** (`Brevo:ApiKey` set;
`Brevo:FromEmail` must be a sender verified in Brevo's dashboard via a 6-digit code — no domain
needed, and it's the only option here that can reach *arbitrary* recipients without one) >
**SMTP** (`Smtp:Username`+`Smtp:Password` both set — e.g. Gmail with an app password; **confirmed
non-viable on Railway** below the Pro plan, which blocks outbound SMTP ports 25/465/587/2525
entirely — works fine for local dev, where no such restriction applies) > **Resend**
(`Resend:ApiKey` set; without a verified domain, delivers only to the Resend account owner's own
address) > `DevEmailSender` (no-op, logs only — used by the test suite and any environment with
none of the above configured). See [Deployment (Railway)](#deployment-railway) for the SMTP-port
finding and current production state.
