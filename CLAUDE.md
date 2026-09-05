# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Stack

**Backend**: `backend/` — ASP.NET Core 9 Web API (C#) + Entity Framework Core 9 + PostgreSQL  
**Frontend**: `frontend/` — Vite 5 + React 19 + TypeScript SPA

## CI

`.github/workflows/ci.yml` runs on every push to `master` and every PR: `dotnet build`/`test`
for the backend (SQLite in-memory, no Postgres service needed — see backend test docs below)
and `tsc`/`vitest`/`vite build` for the frontend, on GitHub-hosted runners (Node 22, .NET 9).
CI-only for now — no deploy step, since there's no production environment yet. Playwright E2E
is intentionally not in CI: it needs a live backend against a real Postgres instance plus the
Vite dev server, which is meaningfully more orchestration than the build/test jobs above.

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
npx.cmd tsc --noEmit                 # Type check
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
- **E2E** (`e2e/*.spec.ts`): `@playwright/test`, Chromium only. Each test seeds its own barber via
  direct API calls (register/login/create-service) rather than relying on existing data, so
  it's safe to run against the same DB repeatedly. `vite.config.ts`'s `test.exclude` keeps
  vitest from also picking up these `.spec.ts` files (both tools default to the same glob).
  `barber-login-gate.spec.ts` additionally needs local dev's backend `Twilio:AuthToken` user-secret
  set to `"test-auth-token"` (or export `TWILIO_AUTH_TOKEN` to match whatever it's actually set to)
  — it signs a real webhook request against that value, same as production's signature check.

## Project Structure

```
barber-saas/
├── backend/
│   ├── Controllers/
│   │   ├── AuthController.cs              # POST /api/auth/register|login|verify-email|resend-verification (barber accounts)
│   │   ├── AdminController.cs             # Protected admin CRUD (JWT required, BarberOnly policy)
│   │   ├── BarbersController.cs           # GET /api/barbers/search|followed, POST/DELETE .../follow (CustomerOnly)
│   │   ├── BookingController.cs           # Public booking API (GetAppointment/etc. accept anonymous)
│   │   ├── CustomerAuthController.cs      # POST /api/customer/auth/whatsapp — redeems a WhatsApp booking-link token into a customer session
│   │   ├── CustomerAppointmentsController.cs  # GET/PATCH /api/customer/appointments/* (CustomerOnly)
│   │   ├── RecurringAppointmentsController.cs # GET/POST/DELETE /api/admin/recurring — owner-managed recurring series
│   │   ├── WhatsAppController.cs          # Twilio webhook — service-selection chatbot flow + book/cancel/reschedule keywords
│   │   └── CronController.cs              # GET /api/cron/reminders, /api/cron/generate-recurring, /api/cron/charge-subscriptions
│   ├── Data/AppDbContext.cs         # EF Core DbContext, indexes, relationships
│   ├── DTOs/AuthDtos.cs             # All request/response record types
│   ├── Migrations/                  # EF migrations
│   ├── Models/
│   │   ├── Barber.cs                # Barber, Service, ServiceGalleryPhoto, Appointment, WorkingHours, Break, BlockedSlot, Customer, RecurringSeries, RecurringSkip, etc.
│   │   ├── CustomerAccount.cs       # Logged-in customer identity (phone-based)
│   │   ├── WhatsAppBookingToken.cs  # Opaque, DB-backed booking-link token (barber+service+phone), 24h-reusable, issued once a service is picked in WhatsApp
│   │   ├── WhatsAppConversationState.cs # Short-lived (barber, phone) "awaiting service selection" row -- Twilio webhooks are stateless per-message
│   │   ├── BarberEmailOtp.cs        # One-time codes for barber email verification
│   │   ├── BarberPasswordResetOtp.cs # One-time codes for barber password reset (mirrors BarberEmailOtp)
│   │   └── Follow.cs                # CustomerAccount <-> Barber follow relationship
│   ├── Services/
│   │   ├── AvailabilityService.cs      # Slot generation + conflict filtering
│   │   ├── RecurringAppointmentService.cs  # Generates real Appointment rows for active RecurringSeries (rolling horizon)
│   │   ├── AppointmentStatusHelper.cs  # Computes effective COMPLETED status without touching the DB row
│   │   ├── I18nService.cs              # Server-side translations (EN/AR/HE) for WhatsApp messages
│   │   ├── JwtService.cs               # Barber JWT generation (30-day tokens, HS256)
│   │   ├── CustomerJwtService.cs       # Customer JWT generation (separate "type": "customer" claim)
│   │   ├── PhoneNormalizer.cs          # Normalizes phone numbers to a canonical form for matching
│   │   ├── WhatsAppBookingTokenService.cs  # Issues/resolves WhatsAppBookingToken rows (shared by WhatsAppController and CustomerAuthController)
│   │   ├── IEmailSender.cs / DevEmailSender.cs  # Email delivery abstraction (dev sender no-ops; code goes out via devCode in the API response instead)
│   ├── GlobalExceptionHandler.cs     # Catches unhandled exceptions -> { error } JSON + ILogger, never a bare 500
│   ├── Program.cs                   # App startup, DI registration, middleware pipeline, BarberOnly/CustomerOnly policies
│   ├── appsettings.json             # Base config (prod DB, JWT keys, AppUrl, CronSecret)
│   ├── appsettings.Development.json # Dev overrides (DB = barbersaas_dev, verbose logging)
│   └── Properties/launchSettings.json  # Port 5280, ASPNETCORE_ENVIRONMENT=Development
└── frontend/
    └── src/
        ├── components/
        │   ├── admin/          # AdminLayout, AdminSidebar, WeeklyCalendar, CustomerPicker, NewAppointmentModal
        │   ├── booking/        # BookingWizard (5-step)
        │   ├── customer/       # CustomerAccountNav, LanguageSwitcher
        │   ├── BackButton.tsx           # Browser-history back button, used on all customer pages
        │   ├── ProtectedRoute.tsx       # Guards /admin/* routes (barber auth)
        │   └── CustomerProtectedRoute.tsx  # Guards customer routes; renders an inline "message us on WhatsApp" notice when unauthenticated (no login page to redirect to)
        ├── lib/
        │   ├── api.ts          # Axios instance — baseURL /api, JWT request interceptor, 401 auto-logout
        │   ├── auth.tsx        # AuthContext + AuthProvider + useAuth hook (barber/admin auth)
        │   ├── customerAuth.tsx  # CustomerAuthProvider + useCustomerAuth hook (loginWithWhatsAppToken + language pref)
        │   └── i18n.ts          # Client-side translations (EN/AR/HE) + t() + serviceName()
        └── pages/
            ├── admin/        # LoginPage, RegisterPage, DashboardPage,
            │                 #   AppointmentsPage, RecurringAppointmentsPage, ServicesPage, SchedulePage, SettingsPage
            └── public/       # BarberPage, BookPage, AppointmentPage, WhatsAppLandingPage,
                              #   BrowseBarbersPage (search + followed list), MyBookingsPage
```

## Architecture

Multi-tenant SaaS. Each barber is a **tenant** identified by a URL slug.

### Backend API Routes

**Auth (no JWT)**
- `POST /api/auth/register` — create barber account (`EmailVerified = false`); auto-creates Mon–Fri 09:00–18:00 working hours; sends a 6-digit email verification code (`devCode` in the response body in Development, matching `DevEmailSender`'s no-op-in-dev pattern)
- `POST /api/auth/login` — returns JWT token (30 days); **403 `{ emailNotVerified: true }`** if the barber hasn't verified their email yet (frontend responds by requesting a fresh code and dropping into the verify-code view)
- `POST /api/auth/verify-email` — `{ email, code }`; marks the barber verified and returns a JWT (`LoginResponse`), logging them in directly
- `POST /api/auth/resend-verification` — `{ email }`; 45s cooldown + 5/hour cap
- `POST /api/auth/forgot-password` — `{ email }`; sends a 6-digit reset code (`devCode` in Development), same cooldown/cap as email verification; 404 if the email isn't registered
- `POST /api/auth/reset-password` — `{ email, code, newPassword }`; verifies the code, updates the password, and returns a JWT (`LoginResponse`), logging them in directly

**Admin (JWT required — barber ID read from token claims, never from body, `BarberOnly` policy)**
- `GET/PATCH /api/admin/settings` — barber profile, language, booking limits (WhatsApp number is read-only here — see [Twilio / WhatsApp](#twilio--whatsapp))
- `GET/POST /api/admin/services` — services CRUD (includes `photoMode` + `galleryPhotos`)
- `PATCH/DELETE /api/admin/services/{id}` — update / soft-delete (IsActive = false)
- `POST /api/admin/services/{id}/gallery` — upload a gallery reference photo (JPG/PNG/WEBP, 5MB max)
- `DELETE /api/admin/services/{id}/gallery/{photoId}` — remove a gallery photo
- `GET/POST /api/admin/schedule` — working hours (upsert by DayOfWeek)
- `POST/DELETE /api/admin/schedule/breaks/{id}` — recurring breaks
- `POST/DELETE /api/admin/schedule/blocked/{id}` — one-off blocked dates/slots
- `GET /api/admin/dashboard?week=0` — weekly appointments (week offset from current)
- `GET /api/admin/appointments?filter=today|upcoming|past` — appointment list (omit `filter` for all); each row includes `recurringSeriesId` (null for one-off bookings)
- `PATCH /api/admin/appointments/{id}` — cancel only (`{ status: "CANCELLED" }`); any other status is rejected — see [Appointment status](#appointment-status-no-manual-complete)
- `GET /api/admin/appointments/availability?date=&serviceId=` — same slot computation as the public `GET /api/{slug}/availability`, but scoped by the JWT's `BarberId` instead of a slug (keeps `AdminController` self-contained); backs both the New Appointment modal's slot grid and the recurring-series form's slot grid
- `GET /api/admin/customers/search?query=` — search this barber's own `Customer` rows by name/phone (min 2 chars, top 20); backs `CustomerPicker`'s autocomplete
- `POST /api/admin/appointments` — owner books directly on a customer's behalf (existing `customerId` or new `customerName`+`customerPhone`, upserted by phone same as public booking); `MaxBookingsPerDay/Week` limits are **not** enforced here (they exist to stop customers gaming self-service booking, not to restrict the owner). `force: true` skips the working-hours/breaks/blocked-slot check (for walk-ins) but still hard-rejects an exact overlap with an existing appointment — see [Owner-created & recurring appointments](#owner-created--recurring-appointments)
- `GET /api/admin/recurring` — list this barber's recurring series (active and inactive), each with its 5 most recent `RecurringSkip` entries and a computed `nextOccurrenceDate`
- `POST /api/admin/recurring` — create a weekly recurring series; immediately generates real `Appointment` rows for the rolling horizon (not just the next one) rather than waiting for the next cron run
- `DELETE /api/admin/recurring/{id}` — deletes the series **and cancels every not-yet-completed appointment it generated** (frees the slot for other bookings); already-completed appointments are left untouched as history

**Public booking (no JWT — `{slug}` identifies the tenant)**
- `GET /api/{slug}/info` — barber name, services, active days, isRTL flag
- `GET /api/{slug}/availability?date=&serviceId=` — available 30-min slots
- `POST /api/{slug}/appointments/photo` — upload a reference photo for a `CustomerUpload`-mode service (anonymous, guest booking allowed); returns `{ url }` to pass as `customerPhotoUrl` below
- `POST /api/{slug}/appointments` — book appointment; returns `{ appointmentId, cancelToken }`; auto-follows the barber if the caller is a logged-in customer; if the service's `photoMode` is `OwnerGallery`/`CustomerUpload`, `galleryPhotoId`/`customerPhotoUrl` respectively is required
- `GET /api/{slug}/appointments/{id}` — view appointment details (used by the public magic-link page)
- `DELETE /api/{slug}/appointments/{id}?token=` — cancel (validated by cancelToken)
- `PATCH /api/{slug}/appointments/{id}?token=` — reschedule (re-checks availability first)

**Customer auth (no JWT)**
- `POST /api/customer/auth/whatsapp` — `{ token }`; redeems a `WhatsAppBookingToken` (issued by `WhatsAppController` once the customer picks a service in the chatbot) into a customer session — returns a customer JWT (`"type": "customer"` claim) plus `{ barberSlug, serviceId }` so the frontend can land directly on date selection with the service preselected. 400 if the token is missing/expired, 404 if the barber/service it points at is gone. See [Customer login via WhatsApp](#customer-login-via-whatsapp).

**Barbers directory**
- `GET /api/barbers/search?query=` — search barbers by name/slug (public, no auth)
- `GET /api/barbers/followed`, `POST/DELETE /api/barbers/{slug}/follow` — manage followed barbers (customer JWT required, `CustomerOnly` policy)

**Customer account (customer JWT required, `CustomerOnly` policy)**
- `GET /api/customer/appointments?filter=` — this customer's appointment history across all barbers, matched by phone
- `POST /api/customer/appointments/{id}/cancel`, `PATCH /api/customer/appointments/{id}/reschedule`, `PATCH /api/customer/appointments/{id}/notes`
- `PATCH /api/customer/appointments/{id}/photo` — change a CONFIRMED appointment's reference photo (only for services with `photoMode != None`); same `galleryPhotoId`/`customerPhotoUrl` shape as booking

**Integrations**
- `POST /api/whatsapp/webhook` — Twilio webhook; validates X-Twilio-Signature; drives the service-selection chatbot flow (see below) plus cancel/reschedule keywords, in the barber's language
- `GET /api/cron/reminders` — send 24h WhatsApp reminders; requires `Authorization: Bearer <CronSecret>`
- `GET /api/cron/generate-recurring` — extends every active `RecurringSeries`' generated `Appointment` rows to the rolling horizon (default 8 weeks, `RecurringGeneration:HorizonWeeks` config); same `Authorization: Bearer <CronSecret>` gate, response shape `{ total, created, skipped }`; meant to run daily via an external scheduler — see [Owner-created & recurring appointments](#owner-created--recurring-appointments)
- `GET /api/cron/charge-subscriptions` — charges every `ACTIVE` barber with a stored `CardcomToken` whose `CardcomNextChargeAt` has passed, via Cardcom's token-charge API; same `Authorization: Bearer <CronSecret>` gate, response shape `{ total, charged, failed }`; a successful charge bumps `CardcomNextChargeAt` by 1 month, a failed one sets `SubscriptionStatus = EXPIRED` — see [Billing (Cardcom)](#billing-cardcom)

### Frontend Routes
- `/` — landing/marketing page
- `/admin/login`, `/admin/register`, `/admin/forgot-password` — auth pages
- `/admin/dashboard` — weekly calendar view; recurring appointments render in purple (instead of the usual confirmed-blue) with a 🔁 badge
- `/admin/appointments` — appointments table with a two-row filter bar: date range (today/upcoming/past/all, server-side) plus client-side name/phone search, service, status, and recurring-vs-one-time filters (all combinable), a live filtered/total count, and a "Clear Filters" link
- `/admin/recurring` — manage recurring series: create (service → customer → day-of-week button → real availability slot grid → notes) and delete (no pause/resume — see below)
- `/admin/schedule` — working hours, breaks, blocked dates
- `/admin/services` — services CRUD
- `/admin/settings` — business info, chatbot customization, read-only assigned WhatsApp number
- `/:slug` — public barber page — **requires a customer session** (see below)
- `/:slug/book` — booking wizard (service → date → time → details) — **requires a customer session**; `?serviceId=` alone (from a WhatsApp booking link) skips service selection straight to date selection, `?serviceId=&date=&time=` (from a waitlist notification) skips straight to the confirm step if the slot's still open
- `/:slug/w/:token` — WhatsApp booking-link landing point (`WhatsAppLandingPage`); redeems the token itself and establishes the session, then redirects into `/:slug/book?serviceId=`; public, not guarded
- `/:slug/appointments/:id?token=<cancelToken>` — view/cancel/reschedule appointment — public, token-secured, no login (opened directly from a WhatsApp/SMS reminder)

### Auth
JWT Bearer token stored in `localStorage`. `api.ts` adds it automatically via request interceptor. 401 responses redirect to `/admin/login` — **except** a 401 from `/auth/login` itself (wrong password), which must NOT redirect or it wipes `LoginPage`'s own error message via a full page reload before React can render it. Admin routes are wrapped in `ProtectedRoute` (`frontend/src/components/ProtectedRoute.tsx`) which checks `useAuth().isAuthenticated`.

**Customer routes**: `/:slug`, `/:slug/book`, `/account/bookings` are wrapped in `CustomerProtectedRoute` (`frontend/src/components/CustomerProtectedRoute.tsx`) — there is no manual sign-in page anymore (see [Customer login via WhatsApp](#customer-login-via-whatsapp)), so an anonymous visitor here just sees an inline "message us on WhatsApp for a booking link" notice instead of a redirect. There is deliberately no guest-browsing fallback for these routes either — this carries forward the earlier "guest booking must work" reversal from the customer-accounts feature. The backend (`BookingController.BookAppointment`) still technically accepts anonymous requests; only the frontend routing enforces a session. `/:slug/appointments/:id` (the magic-link view) and `/:slug/w/:token` (the WhatsApp landing point, which establishes the session itself) are intentionally left outside this guard.

**Following** has no dedicated page/route (`/account/following` was removed) — `BrowseBarbersPage` (`/browse`) fetches `GET /api/barbers/followed` itself and renders a "Barbers You Follow" list right under the search bar, with a "Remove" button per entry. A customer is auto-followed to a barber the moment they book an appointment while logged in (`BookingController.BookAppointment`), not just via an explicit Follow click — guest bookings don't create a follow (no account to attach it to).

### i18n (Translations)
- **Frontend**: `frontend/src/lib/i18n.ts` — typed `const` object with EN/AR/HE strings.  
  Use `t(lang, 'key')` for UI strings and `serviceName(service, lang)` for multilingual service names.  
  **Customer-facing pages** (browse/account/*, a barber's public page, the booking wizard) use the
  customer's own language preference — `useCustomerAuth().language`/`setLang()`, stored under
  `localStorage['customerLang']`, defaulting to **Hebrew** when unset. This is independent of, and
  overrides, that specific barber's own configured `language`/`isRTL` (their business's storefront
  setting) — a customer who picks English sees English everywhere, even on a Hebrew-configured
  barber's page. RTL is derived from the customer's chosen language (`AR`/`HE` → `rtl`), not the
  barber's `isRTL` flag. `<LanguageSwitcher />` (`frontend/src/components/customer/`) exposes the
  picker; it's on `CustomerAccountNav`, `BarberPage`, and `BookingWizard`.
  **Admin/barber dashboard pages** are unaffected — they still use `useAuth().language`, set from the
  barber's own `Settings > Language` field, unrelated to any customer's choice.
- **Backend**: `backend/Services/I18nService.cs` — static `T(lang, key, args)` for WhatsApp/reminder messages.

### Back navigation (customer pages)
`frontend/src/components/BackButton.tsx` — browser-history back (`navigate(-1)`), not a fixed
route, so it works regardless of how the customer arrived. Used on every customer-facing page
(BarberPage, BookPage/BookingWizard step 1, MyBookingsPage, BrowseBarbersPage,
AppointmentPage). BookingWizard steps 2-4 keep their own in-wizard step-back button instead
(moving between wizard steps, not pages).

### Per-customer booking limits
A barber can cap how many times the *same customer* (matched by phone — applies whether they're
logged in or booking as a guest, so it can't be dodged by not signing in) can book with them:
`Barber.MaxBookingsPerDay` / `MaxBookingsPerWeek` (nullable int, `null` = unlimited), set via
`Settings > Booking Limits`. Enforced in `BookingController.BookAppointment` before creating the
appointment — "per week" means the fixed Sun–Sat calendar week containing the requested date.
Reschedules are not currently checked against the limit (only new bookings).

### Service reference photos
Each `Service` has a `PhotoMode` (`None` / `OwnerGallery` / `CustomerUpload`), set per-service on
`Settings > Services`. `OwnerGallery` lets the barber upload a set of style photos
(`ServiceGalleryPhoto`, cascade-deleted with the service) that the customer picks one of when
booking; `CustomerUpload` instead lets the customer upload their own reference photo from their
device. Both are **required**, not optional, when enabled — `BookingController.BookAppointment`
rejects the booking with 400 if the required `galleryPhotoId`/`customerPhotoUrl` is missing (a
gallery photo ID is also validated to belong to the requested service, not just exist anywhere).
The resolved photo URL is stored on `Appointment.PhotoUrl` (nullable, `None` mode leaves it null)
and surfaced back to the barber in the admin appointments table, the Dashboard's appointment-detail
modal (`WeeklyCalendar`), and to both parties in appointment detail views. The customer can change
it later — while the appointment is still CONFIRMED — from either "My Appointments With This
Business" (`BarberPage`) or "My Bookings" (`MyBookingsPage`); both render the shared
`AppointmentCard`, whose "Change Photo" action calls `PATCH /api/customer/appointments/{id}/photo`.
Uploads reuse the same JPG/PNG/WEBP/5MB validation as the barber's own logo upload;
gallery photos live under `wwwroot/uploads/gallery/{serviceId}/`, customer-uploaded reference
photos under `wwwroot/uploads/appointment-photos/` — both served via the existing `/api/uploads`
static file route.

### Owner-created & recurring appointments
Two ways for the barber to book without the customer using the self-service flow:

- **One-off**: `AdminController.CreateAppointment` (`POST /api/admin/appointments`), triggered from the "New Appointment" button on `DashboardPage`/`AppointmentsPage` (`components/admin/NewAppointmentModal.tsx` + `CustomerPicker.tsx`). Same customer upsert-by-phone logic as public booking, but `MaxBookingsPerDay/Week` is not enforced (that limit exists to stop *customers* gaming self-service booking). A `force: true` flag lets the owner book outside normal availability (walk-ins) by skipping the working-hours/breaks/blocked-slot check in `AvailabilityService.GetAvailableSlots`, while still hard-rejecting an exact double-booking via `AvailabilityService.HasConflictingAppointment`.
- **Recurring**: `Models/Barber.cs`'s `RecurringSeries` (barber + customer + service + `DayOfWeek` + `StartTime`, no fixed end date in the UI though the model/DTO still accept an optional `EndDate`) plus `RecurringSkip` (append-only log of dates that couldn't be generated). `Appointment.RecurringSeriesId` (nullable FK, `SetNull` on series delete) links a generated occurrence back to its series — each occurrence is otherwise a fully normal, independently-cancelable `Appointment`; cancelling one has no effect on the series or its other occurrences.
  - **Generation**: `Services/RecurringAppointmentService.GenerateOccurrences()` walks each active series' `LastGeneratedThrough` cursor forward to a rolling horizon (default 8 weeks — `RecurringGeneration:HorizonWeeks`), creating an `Appointment` when `AvailabilityService.GetAvailableSlots` says the slot is free, or logging a `RecurringSkip` (`Reason = "slot_unavailable"`) when it isn't. Generating just-in-time as the cursor reaches each date (not far in advance) is what makes it correctly react to schedule changes made *after* the series was created — e.g. a `BlockedSlot` added 5 weeks out has no `Appointment` row yet, so the occurrence is skipped instead of sitting as a stale conflict. The cursor never moves backward into the past, so resuming/creating after a gap doesn't backfill missed weeks. A series auto-deactivates (`IsActive = false`, logged as a `service_inactive` skip) if its linked `Service` is soft-deleted, or once its `EndDate` (if set) has passed.
  - **Immediate generation on create**: `RecurringAppointmentsController.Create` calls `RecurringAppointmentService.GenerateForSeriesNow(seriesId)` right after saving the series, so the first occurrence(s) exist and block their slot right away — otherwise nothing would appear on the dashboard, and the slot would stay bookable by others, until the next daily cron run.
  - **Deleting a series** (`DELETE /api/admin/recurring/{id}`) cancels every not-yet-completed appointment it generated (`Status = CANCELLED`, freeing the slot) before removing the series row; already-completed history is left untouched (its stored `Status` is always `CONFIRMED` — see [Appointment status](#appointment-status-no-manual-complete) — so the cancel loop checks `AppointmentStatusHelper.EffectiveStatus` per row, not the raw column).
  - **No pause/resume** — deliberately removed; deleting is the only lifecycle action exposed to the owner besides creating. `IsActive` still exists on the model purely for the auto-deactivation cases above.
  - **Creating a series** (`pages/admin/RecurringAppointmentsPage.tsx`): the owner picks a service, a customer (`CustomerPicker`), then a **day-of-week button** (Sun–Sat, not a raw date picker), then a **time slot from the real availability grid** for the nearest upcoming date on that weekday (same `GET /api/admin/appointments/availability` endpoint the one-off modal uses) — never a free-typed time. That computed date becomes the series' `StartDate`.
  - `GET /api/cron/generate-recurring` (`CronController`) is the production trigger — same `CronSecret` bearer-auth pattern as `/api/cron/reminders` — meant to run once daily via an external scheduler.

### Database
EF Core + Npgsql. Dev DB: `barbersaas_dev` (appsettings.Development.json). Prod DB: `barbersaas` (appsettings.json). Auto-migrates in Development on startup.  
All times stored as `"HH:MM"` strings — zero-padded so string comparison is safe.  
Migrations: `InitialCreate` ... `AddRecurringAppointments` (adds `RecurringSeries`, `RecurringSkips`, `Appointments.RecurringSeriesId`) already applied.

### Availability Engine
`Services/AvailabilityService.cs` — generates 30-min slots between working hours start/end, then removes any slot that overlaps with: breaks, blocked slots, or existing CONFIRMED appointments. Also drops slots where `startTime + serviceDuration > workingHours.EndTime`. For **today's date specifically**, also drops any slot whose start time is at or before the current time — a customer booking at 15:00 can't grab a 10:00 slot. `WorkingHours`/`Appointment` start/end times (`"09:00"`, `"17:30"`, ...) are the barber's local wall-clock hours and are never converted to/from UTC anywhere in this app, so "now" is taken as `DateTime.Now` (local server time), not `DateTime.UtcNow` — comparing against UTC would be off by the server's UTC offset (this was a real bug: a customer could book a slot that had already passed).

### Appointment status: no manual "Complete"
The barber can only cancel an appointment now (`AdminController.UpdateAppointmentStatus` rejects any `status` other than `CANCELLED`) — there's no "Mark Complete" button anywhere in the admin UI. Instead, `Services/AppointmentStatusHelper.EffectiveStatus(status, date, endTime)` computes "COMPLETED" automatically for any still-`CONFIRMED` appointment whose end time has passed (compared against `DateTime.Now`, local server time — same reasoning as the Availability Engine above), applied wherever a status is returned to a client: `AdminController` (dashboard + appointments list), `CustomerAppointmentsController.GetMyAppointments`, and `BookingController.GetAppointment` (the magic-link view). `CANCELLED` is never overridden. The stored `AppointmentStatus` column itself stays `CONFIRMED` — only the API response's status string is computed; nothing rewrites the DB row.

### Twilio / WhatsApp
As of 2026-09-05, **one platform-owned Twilio account** handles every barber's WhatsApp chatbot —
not a per-barber account. `Twilio:AccountSid`/`Twilio:AuthToken` (config-only, same
no-default-in-`appsettings.json` pattern as `Jwt:Secret`/`CronSecret` — `dotnet user-secrets`
locally, `Twilio__AccountSid`/`Twilio__AuthToken` env vars in production) are read directly by
`WhatsAppController.Webhook` (inbound signature validation) and `TwilioWhatsAppSender` (outbound
sends, e.g. reminders). The only thing that's still per-barber is `Barber.TwilioNumber` — which of
the platform's Twilio WhatsApp senders that barber's chatbot uses — and it's now **assigned by the
platform admin** (`PATCH /api/platform-admin/barbers/{id}/twilio-number`, a control on
`PlatformAdminBarberDetailPage`), not self-entered by the barber; the barber's own Settings page
just shows it read-only. Twilio is one of Meta's official WhatsApp Business Solution Providers —
messages still run on Meta's actual WhatsApp Business Platform underneath, Twilio just handles the
Meta onboarding/webhook plumbing. **Each WhatsApp number still needs its own Meta WhatsApp Business
Profile approval** regardless of this — that's a Meta requirement tied to the number's business
identity, done manually in the Twilio console per barber, not something this centralization removes
or automates.
- Webhook drives the service-selection chatbot flow (see below) and replies to EN/AR/HE cancel/reschedule keywords by cancelling the next upcoming appointment directly or sending a fresh booking prompt.  
- Reminders are sent by hitting `/api/cron/reminders` (e.g. via an external cron job or scheduler).

### Chatbot customization & language auto-detection
Per barber, in `Settings > WhatsApp Chatbot`:
- `Barber.ChatbotEnabled` (default `true`) — when off, `WhatsAppController.Webhook` returns an
  empty `<Response></Response>` TwiML body (no automated reply at all) for every inbound message,
  cancel/reschedule keywords included — the barber wants to answer customers themselves.
- `Barber.ChatbotWelcomeMessage` / `ChatbotConfirmationMessage` (both nullable free text, one
  language each, not per-EN/AR/HE) — when set, replace the *default* greeting/confirmation text
  only; the service list and its surrounding instructions always stay in the detected language
  (see below), so a custom welcome message is followed by `whatsapp.selectServicePrompt`, not the
  full `whatsapp.selectService` template. A custom confirmation message may include a literal
  `{url}` placeholder to control where the booking link lands in the text; if omitted, the link is
  appended on its own line.

**Language auto-detection** (`WhatsAppController.DetectLanguage`): every inbound message's script
is checked against the Hebrew (`U+0590`–`U+05FF`) and Arabic (`U+0600`–`U+06FF`) Unicode blocks,
falling back to `EN` if it has any Latin letters at all. This is independent of the barber's own
configured storefront `Language` — the bot always replies in whatever language the *customer* just
typed in. A message with no letters at all (a bare numeric reply like `"1"`) carries no signal of
its own, so `ResolveLanguage` falls back to the language already stored on the open
`WhatsAppConversationState` row (see below) for that phone, and only falls back to the barber's own
default when there's no open conversation either (a signal-less first message, e.g. an emoji).
`WhatsAppConversationState.Language` and `WhatsAppBookingToken.Language` both persist the resolved
language — the latter is returned by `POST /api/customer/auth/whatsapp` (`language` field) and the
frontend's `loginWithWhatsAppToken` calls `setLang()` with it, so the booking wizard opens in the
same language the customer was just chatting in, not whatever was last stored in that browser.

### Customer login via WhatsApp
There is no phone+OTP login anymore — a customer session starts by redeeming a link the WhatsApp
bot sent them. Flow (`WhatsAppController` + `CustomerAuthController`):
1. Any message from a phone with no pending selection (or the `book` keyword) gets a numbered list
   of the barber's active services (`whatsapp.selectService`, service order = `Service.Id` order)
   and opens a `WhatsAppConversationState` row (`BarberId`+`Phone`, 10-minute expiry) remembering
   the bot is waiting on a reply — Twilio webhooks are stateless per-message, so this is the only
   way to connect the "which service?" prompt to the customer's numeric reply that follows.
2. A valid numeric reply creates a `WhatsAppBookingToken` (`WhatsAppBookingTokenService.CreateAsync`)
   — an **opaque, DB-backed** id, not a JWT, so the phone number it carries can't be read off the
   URL — and replies with `{AppUrl}/{slug}/w/{token}`. Reusable for 24h (no one-time-use flag), so
   reopening the WhatsApp message later the same day still works. An invalid reply reprompts and
   keeps the state row; the `cancel`/`reschedule` keywords clear it.
3. Opening that URL (`WhatsAppLandingPage`) calls `POST /api/customer/auth/whatsapp` with the
   token, which resolves it (400 if missing/expired, 404 if the barber/service was deleted or
   deactivated since), upserts a `CustomerAccount` by phone — splitting Twilio's `ProfileName` form
   field (the sender's WhatsApp display name) into `Name`/`FamilyName` on the first space, falling
   back to a generic name if WhatsApp didn't supply one — and returns a normal customer JWT via
   `CustomerJwtService.Generate` (identical to the old OTP-verify flow) plus `{ barberSlug,
   serviceId }`. The frontend then redirects into `/:slug/book?serviceId=`, which skips straight to
   date selection (`BookingWizard`'s deep-link `useEffect`) — no sign-up/sign-in step, no service
   list to pick from again.

`PhoneNormalizer.Normalize` (used everywhere phones are stored/matched) keeps a bare local number
as-is if the customer didn't type a `+` — WhatsApp's `From` field always arrives in E.164 already,
so this mainly matters for matching against phones entered elsewhere (owner-created appointments,
the booking form's editable phone field).

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
```

`Jwt:Secret`, `CronSecret`, and `Twilio:AccountSid`/`Twilio:AuthToken` are **not** in `appsettings.json` — there's no default, so the app fails fast (or, for the Twilio pair, simply can't validate/send) if they're missing rather than silently falling back to a guessable value.
- **Local dev**: stored in the `dotnet user-secrets` store for `backend/BarberSaas.Api.csproj` (`UserSecretsId` in the `.csproj`, values live outside the repo at `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`). `dotnet run` loads them automatically in Development.
- **Production**: supply via environment variables (`Jwt__Secret`, `CronSecret`, `Twilio__AccountSid`, `Twilio__AuthToken`) or `appsettings.Production.json` (gitignored) — never commit real values.
- Rotating `Jwt:Secret`/`CronSecret` invalidates all existing JWTs/cron callers signed with the old value — expected, not a bug. Rotating the Twilio pair (e.g. after switching Twilio accounts) requires re-pointing every barber's `TwilioNumber` too if the numbers themselves moved to a different account.

### Billing (Cardcom)
Billed via Cardcom (an Israeli payment gateway) using its "Low Profile" hosted-payment-page API (v11: `https://secure.cardcom.solutions/api/v11/...`), through `Services/ICardcomService`/`CardcomService.cs` (hand-rolled `HttpClient` wrapper -- Cardcom has no official .NET SDK, unlike Stripe.net which this replaced). `Cardcom:TerminalNumber`/`ApiName`/`ApiPassword` ship as empty strings in `appsettings.json` (no Cardcom account exists yet) — `BillingController` checks for `TerminalNumber`/`ApiName` and returns `503 { error: "Payments are not yet configured..." }` instead of attempting a call when they're blank. Once a real Cardcom account exists, set all three the same way as `Jwt:Secret`/`CronSecret`: `dotnet user-secrets` locally, environment variables (`Cardcom__TerminalNumber`, `Cardcom__ApiName`, `Cardcom__ApiPassword`) in production.

Unlike Stripe, Cardcom has no server-side "Subscription" object that auto-recurs — the initial payment (`Operation=ChargeAndCreateToken`) also mints a reusable charge token, which the app's own cron job (`GET /api/cron/charge-subscriptions`, see above) charges again every billing cycle.

- `POST /api/billing/checkout-session` (`BarberOnly`) calls `CreateLowProfileAsync` (`ReturnValue = barber.Id`, so the webhook can resolve the barber directly instead of scanning by a stored customer id) and returns `{ url }` — the Cardcom-hosted payment page — for the frontend to redirect to. Redirects land back on `?billing=success`/`?billing=cancelled`.
- `POST /api/billing/webhook` (anonymous) — Cardcom's webhook has no HMAC signature like Stripe's, so the inbound POST is treated only as a trigger carrying a `LowProfileId`; the handler then calls `GetLowProfileResultAsync` server-to-server to fetch the **verified** result and only acts on that, never on fields taken directly from the webhook body. On a verified result with a `TokenNumber`, sets `Barber.CardcomToken`/`CardcomNextChargeAt` (+1 month) and flips `SubscriptionStatus` to `ACTIVE`. `Barber.CardcomLastLowProfileId` guards against a duplicate webhook redelivery re-processing the same `LowProfileId`.
- `SettingsPage.tsx`'s "Subscribe Now" button (shown whenever `subscriptionStatus !== 'ACTIVE'`) calls the checkout-session endpoint and redirects the browser to the returned Cardcom URL; on return with `?billing=success` it shows a brief banner and refetches settings (immediately and again ~3s later) since `ACTIVE` arrives asynchronously via the webhook, not synchronously on redirect.
- Several exact Cardcom JSON field/endpoint names (`GetLpResult`'s path, the recurring token-charge call's shape) are best-effort reconstructions flagged with comments in `CardcomService.cs` — Cardcom's docs are a JS-rendered SPA that couldn't be scraped when this was built. Verify against `https://secure.cardcom.solutions/Api/v11/Docs` or their Postman collection, and smoke-test against their public sandbox (Terminal `1000`, ApiName `demo`, card `4580000000000000`), before relying on this in production.
