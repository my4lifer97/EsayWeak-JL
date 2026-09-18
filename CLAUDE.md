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
- **E2E** (`e2e/*.spec.ts`): `@playwright/test`, Chromium only. Each test seeds its own business via
  direct API calls (register/login/create-item) rather than relying on existing data, so
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
│   │   ├── AuthController.cs              # POST /api/auth/register|login|verify-email|resend-verification|forgot-password|reset-password|change-password (business owner accounts; login accepts email OR generated username)
│   │   ├── AdminController.cs             # Protected admin CRUD (JWT required, BusinessOnly policy)
│   │   ├── BusinessesController.cs        # GET /api/businesses/search|cities|followed|{slug}/reviews, POST/DELETE .../follow — public directory + reviews list (follow endpoints CustomerOnly)
│   │   ├── BusinessOwnerRequestsController.cs # GET /api/business-types, POST /api/business-owner-requests — public submission side of the admin-approved onboarding flow
│   │   ├── ReviewsController.cs           # api/reviews — CustomerOnly: eligibility, create (403 no completed appt / 409 dup), author-scoped PATCH/DELETE
│   │   ├── BookingController.cs           # Public booking API (GetAppointment/etc. accept anonymous); GET /api/{slug}/info also carries city/address/map + rating aggregate
│   │   ├── CustomerAuthController.cs      # POST /api/customer/auth/whatsapp (booking-link token) + /otp + /verify (direct phone+OTP, a parallel entry point) — both mint the same kind of customer session
│   │   ├── CustomerAppointmentsController.cs  # GET/PATCH /api/customer/appointments/* (CustomerOnly)
│   │   ├── RecurringAppointmentsController.cs # GET/POST/DELETE /api/admin/recurring — owner-managed recurring series
│   │   ├── WaitlistController.cs          # POST /api/{slug}/waitlist/{appointmentId} — CustomerOnly, joins the waitlist for a booked slot
│   │   ├── WhatsAppController.cs          # bridge-inbound (active) + Twilio webhook (dormant fallback) — item-selection chatbot flow + book/cancel/reschedule keywords
│   │   ├── PlatformAdminController.cs     # api/platform-admin/* — bootstrap/login, business & customer search/detail/impersonate/activity, business-owner-request review, review moderation
│   │   ├── BillingController.cs           # POST /api/billing/checkout-session|webhook (Cardcom)
│   │   └── CronController.cs              # GET /api/cron/reminders, /api/cron/generate-recurring, /api/cron/charge-subscriptions
│   ├── Data/AppDbContext.cs         # EF Core DbContext, indexes, relationships
│   ├── DTOs/AuthDtos.cs             # All request/response record types
│   ├── Filters/
│   │   ├── ActivityLogFilter.cs             # Global action filter — writes an ActivityLog row for every authenticated write request
│   │   └── RequirePasswordChangeFilter.cs   # Global — blocks BusinessOnly actions while MustChangePassword is set, except [AllowWithPendingPasswordChange]
│   ├── Migrations/                  # EF migrations
│   ├── Models/
│   │   ├── Business.cs              # Business (tenant), BusinessTypeDefinition, Item, ItemGalleryPhoto, WorkingHours, Break, BlockedSlot, Customer, Appointment, RecurringSeries, RecurringSkip, WaitlistEntry — see enums at the top of the file
│   │   ├── CustomerAccount.cs       # Logged-in customer identity (phone-based)
│   │   ├── BusinessOwnerRequest.cs  # Public request for an account, reviewed by a platform admin — see the onboarding section below
│   │   ├── PlatformAdmin.cs         # Platform staff account (separate login/JWT from Business and CustomerAccount)
│   │   ├── ActivityLog.cs           # Audit trail row written by ActivityLogFilter / impersonation actions
│   │   ├── WhatsAppBookingToken.cs  # Opaque, DB-backed booking-link token (business+item+phone), 24h-reusable, issued once an item is picked in WhatsApp
│   │   ├── WhatsAppConversationState.cs # Short-lived (business, phone) row -- rule-based path's "awaiting item selection" flag, or the AI path's rolling chat history (HistoryJson)
│   │   ├── BusinessEmailOtp.cs      # One-time codes for business email verification
│   │   ├── BusinessPasswordResetOtp.cs # One-time codes for business password reset (mirrors BusinessEmailOtp)
│   │   ├── Review.cs                # Customer review of a business (see Reviews below)
│   │   └── Follow.cs                # CustomerAccount <-> Business follow relationship
│   ├── Services/
│   │   ├── AvailabilityService.cs      # Slot generation + conflict filtering
│   │   ├── ReviewService.cs            # HasCompletedAppointment / MostRecentCompletedAppointment / RecomputeAggregate (denormalized Business.RatingCount/RatingAverage from non-hidden rows)
│   │   ├── RecurringAppointmentService.cs  # Generates real Appointment rows for active RecurringSeries (rolling horizon)
│   │   ├── AppointmentCancellationService.cs # Shared funnel for every cancel path — branches on Business.RequireApprovalOnCustomerCancel, triggers waitlist notify
│   │   ├── WaitlistService.cs          # Notifies waiting customers when a cancellation frees a slot
│   │   ├── FollowService.cs            # Idempotent follow/unfollow (EnsureFollowed etc.)
│   │   ├── AppointmentStatusHelper.cs  # Computes effective COMPLETED status without touching the DB row
│   │   ├── I18nService.cs              # Server-side translations (EN/AR/HE) for WhatsApp messages
│   │   ├── JwtService.cs               # Business JWT generation (30-day tokens, HS256; carries mustChangePassword claim)
│   │   ├── CustomerJwtService.cs       # Customer JWT generation (separate "type": "customer" claim)
│   │   ├── PlatformAdminJwtService.cs  # Platform admin JWT generation (separate "type": "platform_admin" claim)
│   │   ├── UsernameGenerator.cs        # Derives+dedupes the login username for an admin-approved account (firstname + first 2 letters of family name)
│   │   ├── SlugValidator.cs            # Shared slug format/reserved-word rules for self-service and admin-issued signup
│   │   ├── PhoneNormalizer.cs          # Normalizes phone numbers to a canonical form for matching
│   │   ├── WhatsAppBookingTokenService.cs  # Issues/resolves WhatsAppBookingToken rows (shared by WhatsAppController and CustomerAuthController)
│   │   ├── IOpenAiChatClient.cs / OpenAiChatClient.cs  # Wraps the OpenAI Chat Completions API (tool calling) for the optional AI-driven WhatsApp chatbot layer -- pure transport, no DB access
│   │   ├── IEmailSender.cs / DevEmailSender.cs / SmtpEmailSender.cs / ResendEmailSender.cs / BrevoEmailSender.cs  # Email delivery abstraction — precedence Brevo > SMTP > Resend > dev no-op, see Configuration
│   │   └── ICardcomService.cs / CardcomService.cs  # Cardcom billing API client
│   ├── GlobalExceptionHandler.cs     # Catches unhandled exceptions -> { error } JSON + ILogger, never a bare 500
│   ├── Program.cs                   # App startup, DI registration, middleware pipeline, CustomerOnly/BusinessOnly/PlatformAdminOnly policies
│   ├── appsettings.json             # Base config (prod DB, JWT keys, AppUrl, CronSecret)
│   ├── appsettings.Development.json # Dev overrides (DB = barbersaas_dev, verbose logging)
│   └── Properties/launchSettings.json  # Port 5280, ASPNETCORE_ENVIRONMENT=Development
├── whatsapp-bridge/                 # Node.js -- self-hosted WhatsApp transport (Baileys), see its own README.md
│   ├── index.js                     # Express app: /sessions/:businessId/start|status, DELETE /sessions/:businessId, POST /send
│   └── sessionManager.js            # One Baileys socket per linked business; forwards inbound messages to the backend
└── frontend/
    └── src/
        ├── components/
        │   ├── admin/          # AdminLayout, AdminSidebar, WeeklyCalendar, CustomerPicker, NewAppointmentModal
        │   ├── booking/        # BookingWizard (5-step)
        │   ├── customer/       # CustomerAccountNav, LanguageSwitcher, StarRating, BusinessReviews
        │   ├── platform-admin/ # ActivityLogTable
        │   ├── BackButton.tsx           # Browser-history back button, used on all customer pages
        │   ├── ProtectedRoute.tsx       # Guards /admin/* routes (business owner auth)
        │   ├── PlatformAdminProtectedRoute.tsx  # Guards /platform-admin/* routes
        │   └── CustomerProtectedRoute.tsx  # Guards customer routes; renders an inline "message us on WhatsApp" notice when unauthenticated (no login page to redirect to)
        ├── lib/
        │   ├── api.ts          # Axios instance — baseURL /api, JWT request interceptor, 401 auto-logout
        │   ├── auth.tsx        # AuthContext + AuthProvider + useAuth hook (business owner/admin auth)
        │   ├── customerAuth.tsx  # CustomerAuthProvider + useCustomerAuth hook (loginWithWhatsAppToken + language pref)
        │   ├── platformAdminApi.ts / platformAdminAuth.tsx  # Separate axios instance + auth context for the platform-admin panel
        │   └── i18n.ts          # Client-side translations (EN/AR/HE) + t() + itemName()
        └── pages/
            ├── admin/        # LoginPage, RegisterPage, ForgotPasswordPage, SetPasswordPage (forced change on MustChangePassword), DashboardPage,
            │                 #   AppointmentsPage, RecurringAppointmentsPage, ServicesPage, SchedulePage, SettingsPage, ReviewsPage
            ├── platform-admin/  # LoginPage, DashboardPage, BusinessDetailPage, CustomerDetailPage, RequestsPage (business-owner-request review queue)
            └── public/       # HomePage, BusinessPage (public storefront), RequestBusinessAccountPage (public onboarding form),
                              #   BookPage, AppointmentPage, WhatsAppLandingPage, BrowseBusinessesPage (discovery directory: category/city/sort + followed list), MyBookingsPage
```

## Architecture

Multi-tenant SaaS. Each **business** is a tenant identified by a URL slug (originally single-vertical
"barber shop" software; see [Multi-vertical](#multi-vertical-business-types--models) for how that
generalized). The internal C# type/table name is still `Item` for what a customer books ("Service"
in older docs/commits) — bookable and showcase-only items are the same table, distinguished by
`Item.IsBookable`.

### Backend API Routes

**Auth (no JWT)**
- `POST /api/auth/register` — self-service business signup (`EmailVerified = false`); auto-creates Mon–Fri 09:00–18:00 working hours for an `Appointment`/`Both`-model business; sends a 6-digit email verification code (`devCode` in the response body in Development, matching `DevEmailSender`'s no-op-in-dev pattern)
- `POST /api/auth/login` — `{ email, password }` where the `email` field's value is matched against either the business's real email **or** its system-generated `Username` (field name kept as `email` for compatibility — see [Business owner onboarding](#business-owner-onboarding-self-service--admin-approved)); returns JWT token (30 days, carries a `mustChangePassword` claim); **403 `{ emailNotVerified: true }`** if not yet verified (frontend responds by requesting a fresh code and dropping into the verify-code view)
- `POST /api/auth/verify-email` — `{ email, code }`; marks the business verified and returns a JWT (`LoginResponse`), logging them in directly
- `POST /api/auth/resend-verification` — `{ email }`; 45s cooldown + 5/hour cap
- `POST /api/auth/forgot-password` — `{ email }`; sends a 6-digit reset code (`devCode` in Development), same cooldown/cap as email verification; 404 if the email isn't registered
- `POST /api/auth/reset-password` — `{ email, code, newPassword }`; verifies the code, updates the password, and returns a JWT (`LoginResponse`), logging them in directly
- `POST /api/auth/change-password` (`BusinessOnly`, `[AllowWithPendingPasswordChange]`) — `{ currentPassword, newPassword }`; the only `BusinessOnly` action reachable while `MustChangePassword` is set (an admin-issued temp password) — clears the flag and returns a fresh token without the `mustChangePassword` claim

**Admin (JWT required — business ID read from token claims, never from body, `BusinessOnly` policy)**
- `GET/PATCH /api/admin/settings` — business profile, language, booking limits, and discovery fields (`city`, `addressLine`, `mapUrl` free text; `isListed` directory toggle — `mapUrl` must start `http(s)://`). WhatsApp number is read-only here — see [WhatsApp chatbot: self-hosted via Baileys](#whatsapp-chatbot-self-hosted-via-baileys-whatsapp-bridge)
- `GET /api/admin/reviews` — this business's reviews (incl. hidden), newest first, with reviewer name + linked item name
- `POST/DELETE /api/admin/reviews/{id}/reply` — set / clear the owner's public reply
- `GET/POST /api/admin/services` — items CRUD (includes `photoMode` + `galleryPhotos`; route name kept as `services` for URL/frontend stability even though the model is `Item`)
- `PATCH/DELETE /api/admin/services/{id}` — update / soft-delete (IsActive = false)
- `POST /api/admin/services/{id}/gallery` — upload a gallery reference photo (JPG/PNG/WEBP, 5MB max)
- `DELETE /api/admin/services/{id}/gallery/{photoId}` — remove a gallery photo
- `GET/POST /api/admin/schedule` — working hours (upsert by DayOfWeek)
- `POST/DELETE /api/admin/schedule/breaks/{id}` — recurring breaks
- `POST/DELETE /api/admin/schedule/blocked/{id}` — one-off blocked dates/slots
- `GET /api/admin/dashboard?week=0` — weekly appointments (week offset from current)
- `GET /api/admin/appointments?filter=today|upcoming|past` — appointment list (omit `filter` for all); each row includes `recurringSeriesId` (null for one-off bookings)
- `PATCH /api/admin/appointments/{id}` — cancel only (`{ status: "CANCELLED" }`); any other status is rejected — see [Appointment status](#appointment-status-no-manual-complete)
- `GET /api/admin/appointments/availability?date=&serviceId=` — same slot computation as the public `GET /api/{slug}/availability`, but scoped by the JWT's `BusinessId` instead of a slug (keeps `AdminController` self-contained); backs both the New Appointment modal's slot grid and the recurring-series form's slot grid
- `GET /api/admin/customers/search?query=` — search this business's own `Customer` rows by name/phone (min 2 chars, top 20); backs `CustomerPicker`'s autocomplete
- `POST /api/admin/appointments` — owner books directly on a customer's behalf (existing `customerId` or new `customerName`+`customerPhone`, upserted by phone same as public booking); `MaxBookingsPerDay/Week` limits are **not** enforced here (they exist to stop customers gaming self-service booking, not to restrict the owner). `force: true` skips the working-hours/breaks/blocked-slot check (for walk-ins) but still hard-rejects an exact overlap with an existing appointment — see [Owner-created & recurring appointments](#owner-created--recurring-appointments)
- `GET /api/admin/recurring` — list this business's recurring series (active and inactive), each with its 5 most recent `RecurringSkip` entries and a computed `nextOccurrenceDate`
- `POST /api/admin/recurring` — create a weekly recurring series; immediately generates real `Appointment` rows for the rolling horizon (not just the next one) rather than waiting for the next cron run
- `DELETE /api/admin/recurring/{id}` — deletes the series **and cancels every not-yet-completed appointment it generated** (frees the slot for other bookings); already-completed appointments are left untouched as history

**Public booking (no JWT — `{slug}` identifies the tenant)**
- `GET /api/{slug}/info` — business name, items, active days, isRTL flag, plus `city`/`addressLine`/`mapUrl` and the rating aggregate (`ratingCount`, `ratingAverage`). Resolves by slug **regardless of `IsListed`** — unlisting only removes a business from the directory; a direct/shared link still works
- `GET /api/{slug}/availability?date=&serviceId=` — available 30-min slots
- `POST /api/{slug}/appointments/photo` — upload a reference photo for a `CustomerUpload`-mode item (anonymous, guest booking allowed); returns `{ url }` to pass as `customerPhotoUrl` below
- `POST /api/{slug}/appointments` — book appointment; returns `{ appointmentId, cancelToken }`; auto-follows the business if the caller is a logged-in customer; if the item's `photoMode` is `OwnerGallery`/`CustomerUpload`, `galleryPhotoId`/`customerPhotoUrl` respectively is required
- `GET /api/{slug}/appointments/{id}` — view appointment details (used by the public magic-link page)
- `DELETE /api/{slug}/appointments/{id}?token=` — cancel (validated by cancelToken) — routes through `AppointmentCancellationService.CancelFromCustomerAsync`, see [Waitlist & cancellation approval](#waitlist--cancellation-approval)
- `PATCH /api/{slug}/appointments/{id}?token=` — reschedule (re-checks availability first)

**Customer auth (no JWT)**
- `POST /api/customer/auth/whatsapp` — `{ token }`; redeems a `WhatsAppBookingToken` (issued by `WhatsAppController` once the customer picks an item in the chatbot) into a customer session — returns a customer JWT (`"type": "customer"` claim) plus `{ businessSlug, itemId }` so the frontend can land directly on date selection with the item preselected. 400 if the token is missing/expired, 404 if the business/item it points at is gone. See [Customer login via WhatsApp](#customer-login-via-whatsapp).
- `POST /api/customer/auth/otp` — `{ phone }`; sends a 6-digit SMS code (`devOtp` in the response body in Development), 45s cooldown + 5/hour cap; returns `{ isNewCustomer }`. `POST /api/customer/auth/verify` — `{ phone, otp, name?, familyName? }`; verifies the code and returns the same customer-JWT shape as the WhatsApp login (`name`/`familyName` required only for a brand-new account). A second, parallel entry point alongside WhatsApp login — see [Customer login via phone+OTP](#customer-login-via-phoneotp).

**Business directory / discovery (no JWT except follow)**
- `GET /api/businesses/search?query=&businessTypeKey=&city=&sort=&page=&pageSize=` — public directory. Base filter `IsListed && SubscriptionStatus != EXPIRED`. `sort`: `rating` (default — avg desc, then count, then name), `popular` (follower count), `newest`, `name`. Returns `PagedResult<BusinessSearchResultDto>` (`{ items, page, pageSize, total, hasMore }`); each item carries `businessTypeKey`, `city`, `ratingAverage`, `ratingCount`, `followerCount`, `isFollowed`. `query`/`city` match case-insensitively via `lower()` (not `EF.Functions.ILike`, which the SQLite test provider can't run)
- `GET /api/businesses/cities` — distinct trimmed cities among listed, non-expired businesses (backs the browse city filter)
- `GET /api/businesses/{slug}/reviews?page=&pageSize=` — public, non-hidden reviews newest-first as `PublicReviewListDto` (rating aggregate + `PagedResult<PublicReviewDto>`); reviewer shown as "First L."
- `GET /api/businesses/followed`, `POST/DELETE /api/businesses/{slug}/follow` — manage followed businesses (customer JWT, `CustomerOnly`)
- `GET /api/business-types` (`BusinessOwnerRequestsController`) — active `BusinessTypeDefinition` rows, ordered by display name; backs both the public request form's picker and the browse category chips

**Business owner onboarding (no JWT except the review side)**
- `POST /api/business-owner-requests` — public submission `{ businessName, ownerFirstName, ownerFamilyName, email, phone, businessTypeId, businessDescription?, systemNeeds? }`; names must be English-letters-only (the username is derived from them); 400 on an email that already has a `Business`, 409 on a second concurrent `Pending` request for the same email; best-effort admin-notification email
- Platform-admin review side is under `/api/platform-admin` — see [Platform admin](#platform-admin) below

**Reviews (customer JWT, `CustomerOnly`)**
- `GET /api/reviews/eligibility?businessSlug=` — `{ canReview, alreadyReviewed, review? }`. `canReview` needs a **completed** appointment (Status `CONFIRMED` + effective end time past, per `AppointmentStatusHelper`; no stored `COMPLETED`) and `!PendingCancellationApproval`
- `POST /api/reviews` — `{ businessSlug, rating (1–5), comment? }`. **403** without a completed appointment, **409** if a review already exists (frontend PATCHes instead). Stamps `AppointmentId` from the most-recent completed visit; recomputes the business aggregate
- `PATCH/DELETE /api/reviews/{id}` — author-scoped (`r.CustomerAccountId == accountId`, else 404); both recompute the aggregate

**Waitlist (customer JWT, `CustomerOnly`)**
- `POST /api/{slug}/waitlist/{appointmentId}` — join the waitlist for a currently-CONFIRMED appointment; 400 if the business hasn't turned `WaitlistEnabled` on; idempotent (a repeat join for the same customer+appointment is a no-op 200, not a duplicate row)

**Customer account (customer JWT required, `CustomerOnly` policy)**
- `GET /api/customer/appointments?filter=` — this customer's appointment history across all businesses, matched by phone
- `POST /api/customer/appointments/{id}/cancel`, `PATCH /api/customer/appointments/{id}/reschedule`, `PATCH /api/customer/appointments/{id}/notes`
- `PATCH /api/customer/appointments/{id}/photo` — change a CONFIRMED appointment's reference photo (only for items with `photoMode != None`); same `galleryPhotoId`/`customerPhotoUrl` shape as booking

**Platform admin (`PlatformAdminOnly` except bootstrap/login)**
- `GET /api/platform-admin/bootstrap-available` — `{ available }`, true until the first admin account exists
- `POST /api/platform-admin/bootstrap` — creates the (single, for now) platform admin account; 403 once one already exists, so it's safe to leave the endpoint public
- `POST /api/platform-admin/login` — email+password, returns a platform-admin JWT (`"type": "platform_admin"` claim, separate from Business/CustomerAccount tokens)
- `GET /api/platform-admin/businesses?search=`, `GET .../businesses/{id}` — search/detail across every tenant
- `POST /api/platform-admin/businesses/{id}/whatsapp/link`, `GET .../whatsapp/status`, `DELETE .../whatsapp/link` — link/poll/unlink a business's self-hosted WhatsApp chatbot session (see [WhatsApp chatbot: self-hosted via Baileys](#whatsapp-chatbot-self-hosted-via-baileys-whatsapp-bridge))
- `GET /api/platform-admin/businesses/{id}/activity`, `GET .../customers/{id}/activity` — that entity's `ActivityLog` rows, newest first, flagged when the acting token was an impersonation
- `POST /api/platform-admin/businesses/{id}/impersonate`, `POST .../customers/{id}/impersonate` — mint a real Business/Customer JWT on their behalf for support purposes; every action taken with it is logged with `ImpersonatedByPlatformAdminId` set
- `GET /api/platform-admin/business-owner-requests?status=`, `GET .../{id}` — review queue for [Business owner onboarding](#business-owner-onboarding-self-service--admin-approved) requests
- `POST /api/platform-admin/business-owner-requests/{id}/approve` — `{ slug }`; creates the `Business` (system-generated `Username`, random temp password, `MustChangePassword = true`, `EmailVerified = true` since the admin already vetted it), emails the credentials best-effort, and **always returns the temp password in the response** so the admin can hand it over manually if the email failed
- `POST /api/platform-admin/business-owner-requests/{id}/reject` — `{ note? }`
- `GET /api/platform-admin/reviews?businessId=`, `POST .../reviews/{id}/hide|unhide` — moderation (toggles `Review.IsHidden`, recomputes the business's rating aggregate)

**Integrations**
- `POST /api/whatsapp/bridge/inbound` — active inbound path; called by the self-hosted whatsapp-bridge service (auth: `X-Bridge-Secret`), resolves the business directly by `BusinessId`, drives the item-selection chatbot flow (see below) plus cancel/reschedule keywords, in the business's language
- `POST /api/whatsapp/webhook` — dormant legacy Twilio webhook (validates X-Twilio-Signature); kept as a fallback path, see [WhatsApp chatbot: self-hosted via Baileys](#whatsapp-chatbot-self-hosted-via-baileys-whatsapp-bridge)
- `GET /api/cron/reminders` — send 24h WhatsApp reminders; requires `Authorization: Bearer <CronSecret>`
- `GET /api/cron/generate-recurring` — extends every active `RecurringSeries`' generated `Appointment` rows to the rolling horizon (default 8 weeks, `RecurringGeneration:HorizonWeeks` config); same `Authorization: Bearer <CronSecret>` gate, response shape `{ total, created, skipped }`; meant to run daily via an external scheduler — see [Owner-created & recurring appointments](#owner-created--recurring-appointments)
- `GET /api/cron/charge-subscriptions` — charges every `ACTIVE` business with a stored `CardcomToken` whose `CardcomNextChargeAt` has passed, via Cardcom's token-charge API; same `Authorization: Bearer <CronSecret>` gate, response shape `{ total, charged, failed }`; a successful charge bumps `CardcomNextChargeAt` by 1 month, a failed one sets `SubscriptionStatus = EXPIRED` — see [Billing (Cardcom)](#billing-cardcom)

### Frontend Routes
- `/` — landing/marketing page (`HomePage`)
- `/admin/login`, `/admin/register`, `/admin/forgot-password` — auth pages
- `/admin/set-password` — forced password-change screen shown while the JWT's `mustChangePassword` claim is set (admin-issued temp password); every other `/admin/*` route redirects here until it's cleared
- `/request-business-account` — public onboarding form (`RequestBusinessAccountPage`) feeding `POST /api/business-owner-requests`
- `/login` — customer phone+OTP sign-in (`CustomerLoginPage`), a second entry point alongside the WhatsApp booking-link flow; `?next=` (default `/browse`) controls where it redirects after verifying — see [Customer login via phone+OTP](#customer-login-via-phoneotp)
- `/admin/dashboard` — weekly calendar view; recurring appointments render in purple (instead of the usual confirmed-blue) with a 🔁 badge
- `/admin/appointments` — appointments table with a two-row filter bar: date range (today/upcoming/past/all, server-side) plus client-side name/phone search, service, status, and recurring-vs-one-time filters (all combinable), a live filtered/total count, and a "Clear Filters" link
- `/admin/recurring` — manage recurring series: create (item → customer → day-of-week button → real availability slot grid → notes) and delete (no pause/resume — see below)
- `/admin/schedule` — working hours, breaks, blocked dates
- `/admin/services` — items CRUD (route/page names kept as "services" — see the note under Admin routes above)
- `/admin/reviews` — this business's reviews with an inline owner-reply editor; hidden reviews shown greyed
- `/admin/settings` — business info, **location & directory** (city/address/map link + "list in directory" toggle), booking limits, waitlist + cancellation-approval toggles, chatbot customization, read-only assigned WhatsApp number
- `/browse` — public discovery directory (`BrowseBusinessesPage`): category chips (`GET /api/business-types`), city + sort filters, a default top-rated list (no "type first" gate), "Load more" paging, rich cards linking to `/:slug`; the authenticated-only "Businesses You Follow" list stays below
- `/:slug` — **public** business storefront (`BusinessPage`) — viewable logged-out (stars, location, reviews); the Follow button is disabled with a WhatsApp-only tooltip for anonymous visitors
- `/:slug/book` — booking wizard (item → date → time → details) — **requires a customer session**; `?serviceId=` alone (from a WhatsApp booking link) skips item selection straight to date selection, `?serviceId=&date=&time=` (from a waitlist notification) skips straight to the confirm step if the slot's still open
- `/:slug/w/:token` — WhatsApp booking-link landing point (`WhatsAppLandingPage`); redeems the token itself and establishes the session, then redirects into `/:slug/book?serviceId=`; public, not guarded
- `/:slug/appointments/:id?token=<cancelToken>` — view/cancel/reschedule appointment — public, token-secured, no login (opened directly from a WhatsApp/SMS reminder)
- `/platform-admin/login` — separate login, its own auth context (`lib/platformAdminAuth.tsx`), not the business/customer one
- `/platform-admin`, `/platform-admin/businesses/:id`, `/platform-admin/customers/:id`, `/platform-admin/requests` — dashboard, entity detail (activity log + impersonate), business-owner-request review queue; guarded by `PlatformAdminProtectedRoute`

### Auth
JWT Bearer token stored in `localStorage`. `api.ts` adds it automatically via request interceptor. 401 responses redirect to `/admin/login` — **except** a 401 from `/auth/login` itself (wrong password), which must NOT redirect or it wipes `LoginPage`'s own error message via a full page reload before React can render it. Admin routes are wrapped in `ProtectedRoute` (`frontend/src/components/ProtectedRoute.tsx`) which checks `useAuth().isAuthenticated`, and further gated by `/admin/set-password` while `mustChangePassword` is set (see [Business owner onboarding](#business-owner-onboarding-self-service--admin-approved)). The platform-admin panel is a fully separate auth stack (`platformAdminAuth.tsx`, `PlatformAdminProtectedRoute`) — its JWT is never sent on business/customer requests or vice versa.

**Customer routes**: `/:slug/book` and `/account/bookings` are wrapped in `CustomerProtectedRoute` (`frontend/src/components/CustomerProtectedRoute.tsx`) — an anonymous visitor to a guarded route sees an inline "message us on WhatsApp" notice **plus** a link into `/login?next=<path>` (phone+OTP, see [Customer login via phone+OTP](#customer-login-via-phoneotp)), not a hard redirect, since there's no single canonical sign-in page the way business/platform-admin auth has. As of the Discovery phase, **`/:slug` and `/browse` are public** (read-only storefront + directory) — moved out of the guard so a logged-out visitor who finds a business can view it; booking still needs a customer session (from either login path). `/:slug/appointments/:id` (the magic-link view) and `/:slug/w/:token` (the WhatsApp landing point, which establishes the session itself) are also outside the guard. `CustomerAccountNav` (shown on customer pages) links to `/login` when logged out, same as the guard's fallback.

**Following** has no dedicated page/route (`/account/following` was removed) — `BrowseBusinessesPage` (`/browse`) fetches `GET /api/businesses/followed` itself and renders a "Businesses You Follow" list below the directory results (authenticated only). A customer is auto-followed to a business the moment they book an appointment while logged in (`BookingController.BookAppointment`), not just via an explicit Follow click — guest bookings don't create a follow (no account to attach it to). The Follow button on `/:slug` and on browse cards is rendered **disabled** (with a WhatsApp-only tooltip) for anonymous visitors, since there's no sign-in page to send them to.

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
- **Backend**: `backend/Services/I18nService.cs` — static `T(lang, key, args)` for WhatsApp/reminder messages.

### Back navigation (customer pages)
`frontend/src/components/BackButton.tsx` — browser-history back (`navigate(-1)`), not a fixed
route, so it works regardless of how the customer arrived. Used on every customer-facing page
(BusinessPage, BookPage/BookingWizard step 1, MyBookingsPage, BrowseBusinessesPage,
AppointmentPage). BookingWizard steps 2-4 keep their own in-wizard step-back button instead
(moving between wizard steps, not pages).

### Per-customer booking limits
A business can cap how many times the *same customer* (matched by phone — applies whether they're
logged in or booking as a guest, so it can't be dodged by not signing in) can book with them:
`Business.MaxBookingsPerDay` / `MaxBookingsPerWeek` (nullable int, `null` = unlimited), set via
`Settings > Booking Limits`. Enforced in `BookingController.BookAppointment` before creating the
appointment — "per week" means the fixed Sun–Sat calendar week containing the requested date.
Reschedules are not currently checked against the limit (only new bookings).

### Returning customer's name is locked after their first booking with a business
`BookingWizard`'s First/Family Name fields are `disabled` (prefilled from the account, same as the
phone field) whenever the customer is authenticated — the customer isn't meant to change the name
they're on file with once they've booked with a business before. Enforced server-side too, not just
in the UI: `BookingController.BookAppointment` only takes the submitted `customerName`/
`customerFamilyName` when creating a brand-new `Customer` row for that (business, phone), or for a
**guest** (no `CustomerAccount`) booking again, who can still correct their own typed name each
time. A logged-in customer's existing `Customer.Name`/`FamilyName` for that business is left alone
on every booking after the first, regardless of what the request body says.

### Item reference photos
Each `Item` has a `PhotoMode` (`None` / `OwnerGallery` / `CustomerUpload` / `Both`), set per-item on
`Settings > Services`. `OwnerGallery` lets the business upload a set of style photos
(`ItemGalleryPhoto`, cascade-deleted with the item) that the customer picks one of when
booking; `CustomerUpload` instead lets the customer upload their own reference photo from their
device. When required by the mode, `BookingController.BookAppointment`
rejects the booking with 400 if the required `galleryPhotoId`/`customerPhotoUrl` is missing (a
gallery photo ID is also validated to belong to the requested item, not just exist anywhere).
The resolved photo URL is stored on `Appointment.PhotoUrl` (nullable, `None` mode leaves it null)
and surfaced back to the business in the admin appointments table, the Dashboard's appointment-detail
modal (`WeeklyCalendar`), and to both parties in appointment detail views. The customer can change
it later — while the appointment is still CONFIRMED — from either "My Appointments With This
Business" (`BusinessPage`) or "My Bookings" (`MyBookingsPage`); both render the shared
`AppointmentCard`, whose "Change Photo" action calls `PATCH /api/customer/appointments/{id}/photo`.
Uploads reuse the same JPG/PNG/WEBP/5MB validation as the business's own logo upload;
gallery photos live under `wwwroot/uploads/gallery/{itemId}/`, customer-uploaded reference
photos under `wwwroot/uploads/appointment-photos/` — both served via the existing `/api/uploads`
static file route.

### Reviews
`Models/Review.cs` (own file) — `Business` + `CustomerAccount` + `Appointment` FKs, `Rating` 1–5,
`Comment?`, `OwnerReply?`/`OwnerRepliedAt?`, `IsHidden` (platform moderation). Unique index
`(CustomerAccountId, BusinessId)` — **one review per customer per business**, edited in place, never
duplicated. `Business.RatingCount`/`RatingAverage` are **denormalized**, recomputed from non-hidden
rows by `ReviewService.RecomputeAggregate` on every review mutation (create/edit/delete/hide) — a
whole-recompute, not incremental deltas (race-tolerant, cheap at this scale); average is 0 when
count is 0.

**Eligibility = a completed appointment only.** `ReviewService.HasCompletedAppointment` materializes
`{ Date, EndTime }` for the customer's `CONFIRMED`, non-`PendingCancellationApproval` appointments at
that business and checks any effective end time is in the past, using the same wall-clock math as
`AppointmentStatusHelper.EffectiveStatus` (there is no stored `COMPLETED`). Showcase-only businesses
therefore can't be reviewed yet. `POST /api/reviews` 403s without one and 409s on a duplicate (the
frontend switches to `PATCH`); `AppointmentId` is stamped from `MostRecentCompletedAppointment` as a
"verified visit" marker.

Frontend: `components/customer/StarRating.tsx` (read-only row, or an editable picker when `onChange`
is passed), `components/customer/BusinessReviews.tsx` (mounted on `BusinessPage`, opens the form when
the URL hash is `#reviews`), a "Leave a review" action on a COMPLETED `AppointmentCard`,
`pages/admin/ReviewsPage.tsx` (`/admin/reviews`) for owner replies, and a hide/unhide list on the
platform-admin business detail page. i18n keys for reviews live in all of EN/AR/HE in `i18n.ts`.

### Discovery (public directory)
`Business` carries `City` / `AddressLine` / `MapUrl` (all free text — no geocoding or lookup table)
and `IsListed` (bool, DB default `true` so every existing tenant stays discoverable). `City` and
`IsListed` are indexed. All four are edited on `Settings > Location` (`MapUrl` validated to start
`http(s)://`).

`BusinessesController.Search` is the directory: base filter `IsListed && SubscriptionStatus !=
EXPIRED`, combinable `query` / `businessTypeKey` / `city` filters, `sort` of `rating` (default) /
`popular` / `newest` / `name`, and a `PagedResult<T>` envelope (`{ items, page, pageSize, total,
hasMore }` — the codebase's one pagination shape, shared with the reviews list). `query`/`city` match
case-insensitively via `lower()` — **not `EF.Functions.ILike`**, which is Npgsql-only and throws
under the SQLite provider the tests use. `GET /api/businesses/cities` feeds the city filter.
`GET /api/{slug}/info` still resolves by slug **regardless of `IsListed`** — unlisting removes a
business from the directory only, a shared link keeps working.

Frontend: `/:slug` (`BusinessPage`) and `/browse` (`BrowseBusinessesPage`) are **public** — moved
out of `CustomerProtectedRoute` (see [Auth](#auth)). `BrowseBusinessesPage` uses `useInfiniteQuery`
against `PagedResult`, shows a default top-rated list (no "type something first" gate), category
chips from `GET /api/business-types`, city + sort `<select>`s, "Load more" paging, and keeps the
authenticated-only "Businesses You Follow" list. Discovery i18n keys are in all of EN/AR/HE.

### Multi-vertical: business types & models
The platform started as barber-shop-only software and generalized in three migrations
(`AddBusinessTypeAndModel` → `RenameBarberToBusiness` → `GeneralizeServiceToItem`) into a generic
appointment/showcase platform:

- **`BusinessTypeDefinition`** — an extensible lookup table of verticals (`key`, `EN`/`AR`/`HE`
  display name, `IsActive`), seeded with rows like `barber`, not a hardcoded enum — adding a new
  vertical is a data insert, not a deploy. `Business.BusinessTypeId` is nullable (existing rows
  predate the column) and required going forward at both self-service register and the
  business-owner-request form.
- **`Business.BusinessModel`** (`Appointment` / `Showcase` / `Both`) — coarse classification of
  whether the business takes bookings at all. `Appointment` is the original/default behavior;
  `Showcase` is a pure catalog with no booking (and, per [Reviews](#reviews), can't be reviewed yet
  since eligibility requires a completed appointment); `Both` mixes the two.
- **`Item`** (the renamed `Service`) — carries `IsBookable` **per item**, independent of
  `Business.BusinessModel`, so a `Both`-model business can mix bookable and showcase-only items in
  one list. `DurationMinutes` and `Price` are both nullable — null duration for a non-bookable item,
  null price meaning "contact for price."

Route paths, DTO field names, and several frontend file/page names (`/admin/services`,
`ServicesPage.tsx`, `serviceId` query params) were **not** renamed along with the model — changing
public URLs and long-lived query-string contracts (WhatsApp booking links, waitlist redirects)
wasn't worth the churn. When reading or writing this code, "service"/"barber" in a route, DTO field,
or old comment means the same thing as `Item`/`Business` in the model.

### Business owner onboarding (self-service & admin-approved)
Two ways a `Business` account comes into existence:

1. **Self-service** — `POST /api/auth/register` (`AuthController`), unchanged in shape since before
   the multi-vertical work: email + password, `EmailVerified = false` until the 6-digit code is
   confirmed. Logs in by email only (`Username` stays null).
2. **Admin-approved** — a prospective owner submits `POST /api/business-owner-requests`
   (`BusinessOwnerRequestsController`, public) with their name, business name/type, and contact
   info; a `BusinessOwnerRequest` row (`Pending`/`Approved`/`Rejected`) is created and a platform
   admin reviews it at `/platform-admin/requests`. Approving
   (`PlatformAdminController.ApproveBusinessOwnerRequest`) picks a slug, generates a unique login
   `Username` (`UsernameGenerator`: first name + first two letters of family name, deduped with a
   numeric suffix against every existing `Business.Username`), a random 12-character temp password
   (ambiguity-free alphabet — no `0/O/1/l/I`), and creates the `Business` with `EmailVerified = true`
   (the admin already vetted it) and **`MustChangePassword = true`**. The credentials are emailed
   best-effort and always also returned in the API response, so the admin can hand them over
   manually if delivery failed.

`RequirePasswordChangeFilter` (global, `Program.cs`) blocks every `BusinessOnly` action for an
account with `MustChangePassword = true` except `AuthController.ChangePassword`
(`[AllowWithPendingPasswordChange]`) — the frontend's `ProtectedRoute` sends such a session straight
to `/admin/set-password`. Both signup paths share `SlugValidator` (format + reserved-word rules) so
neither can produce a slug the other would reject.

### Platform admin
A `PlatformAdmin` account is entirely separate from `Business`/`CustomerAccount` — its own model,
its own JWT (`PlatformAdminJwtService`, `"type": "platform_admin"` claim), its own frontend auth
context/route guard (`lib/platformAdminAuth.tsx`, `PlatformAdminProtectedRoute`), so its token is
never mixed up with a business or customer session. `POST /api/platform-admin/bootstrap` creates the
first (and, for now, only) admin account and always 403s once one exists, so the endpoint can stay
public without becoming an open door — `GET .../bootstrap-available` lets the frontend decide
whether to show the bootstrap form or the login form.

From `/platform-admin`, an admin can search/inspect any `Business` or `CustomerAccount`, review and
approve/reject business-owner requests (see above), moderate reviews (hide/unhide), assign a
business's platform-owned Twilio WhatsApp number, and **impersonate** either a business or a
customer — minting a real JWT for that account so the admin can act as them for support purposes.
Every write made by `ActivityLogFilter` (global action filter, every authenticated write request)
and every impersonation start is recorded as an `ActivityLog` row (`BusinessId`/`CustomerAccountId`,
`Action`, `Description`, method/path/status/IP, and `ImpersonatedByPlatformAdminId` when acting via
an impersonation token) — deliberately request metadata only, never request bodies, so passwords or
Twilio tokens can never end up in a log row. `/platform-admin/businesses/:id` and
`/customers/:id` render that entity's activity feed via `ActivityLogTable`.

### Waitlist & cancellation approval
Two related, independently-toggleable `Business` settings (`Settings > Booking Limits` area):

- **`WaitlistEnabled`** — lets a customer `POST /api/{slug}/waitlist/{appointmentId}` to join the
  waitlist for a slot that's currently booked (`WaitlistEntry`, idempotent per customer+appointment).
  `WaitlistService` notifies waiting customers (by their configured status, `WAITING` →
  `NOTIFIED`/`RESOLVED`) when `AppointmentCancellationService.CancelAsync(notifyWaitlist: true)`
  frees that slot.
- **`RequireApprovalOnCustomerCancel`** — changes what happens when a *customer* cancels (magic-link,
  logged-in "My Bookings", or the WhatsApp `cancel` keyword — all three now route through
  `AppointmentCancellationService.CancelFromCustomerAsync` instead of flipping status directly).
  **Off** (default): cancels immediately, same as before, and auto-notifies the waitlist if enabled.
  **On**: the cancellation doesn't finalize — `Appointment.PendingCancellationApproval` is set true
  and `Status` deliberately **stays `CONFIRMED`** (no new status enum value), so the slot keeps
  blocking availability with zero changes needed to `AvailabilityService`; the business is instead
  sent a WhatsApp message and resolves it manually (offer to waitlist / cancel silently / replace
  customer) from the dashboard. Falls back to immediate-cancel automatically if the business has no
  Twilio number or phone configured (nobody to notify). `AppointmentStatusHelper` computes a
  separate customer-facing status so a frozen appointment still reads as "CANCELLED" to the customer
  despite being `CONFIRMED` internally.

### Owner-created & recurring appointments
Two ways for the business to book without the customer using the self-service flow:

- **One-off**: `AdminController.CreateAppointment` (`POST /api/admin/appointments`), triggered from the "New Appointment" button on `DashboardPage`/`AppointmentsPage` (`components/admin/NewAppointmentModal.tsx` + `CustomerPicker.tsx`). Same customer upsert-by-phone logic as public booking, but `MaxBookingsPerDay/Week` is not enforced (that limit exists to stop *customers* gaming self-service booking). A `force: true` flag lets the owner book outside normal availability (walk-ins) by skipping the working-hours/breaks/blocked-slot check in `AvailabilityService.GetAvailableSlots`, while still hard-rejecting an exact double-booking via `AvailabilityService.HasConflictingAppointment`.
- **Recurring**: `Models/Business.cs`'s `RecurringSeries` (business + customer + item + `DayOfWeek` + `StartTime`, no fixed end date in the UI though the model/DTO still accept an optional `EndDate`) plus `RecurringSkip` (append-only log of dates that couldn't be generated). `Appointment.RecurringSeriesId` (nullable FK, `SetNull` on series delete) links a generated occurrence back to its series — each occurrence is otherwise a fully normal, independently-cancelable `Appointment`; cancelling one has no effect on the series or its other occurrences.
  - **Generation**: `Services/RecurringAppointmentService.GenerateOccurrences()` walks each active series' `LastGeneratedThrough` cursor forward to a rolling horizon (default 8 weeks — `RecurringGeneration:HorizonWeeks`), creating an `Appointment` when `AvailabilityService.GetAvailableSlots` says the slot is free, or logging a `RecurringSkip` (`Reason = "slot_unavailable"`) when it isn't. Generating just-in-time as the cursor reaches each date (not far in advance) is what makes it correctly react to schedule changes made *after* the series was created — e.g. a `BlockedSlot` added 5 weeks out has no `Appointment` row yet, so the occurrence is skipped instead of sitting as a stale conflict. The cursor never moves backward into the past, so resuming/creating after a gap doesn't backfill missed weeks. A series auto-deactivates (`IsActive = false`, logged as a `service_inactive` skip) if its linked `Item` is soft-deleted, or once its `EndDate` (if set) has passed.
  - **Immediate generation on create**: `RecurringAppointmentsController.Create` calls `RecurringAppointmentService.GenerateForSeriesNow(seriesId)` right after saving the series, so the first occurrence(s) exist and block their slot right away — otherwise nothing would appear on the dashboard, and the slot would stay bookable by others, until the next daily cron run.
  - **Deleting a series** (`DELETE /api/admin/recurring/{id}`) cancels every not-yet-completed appointment it generated (`Status = CANCELLED`, freeing the slot) before removing the series row; already-completed history is left untouched (its stored `Status` is always `CONFIRMED` — see [Appointment status](#appointment-status-no-manual-complete) — so the cancel loop checks `AppointmentStatusHelper.EffectiveStatus` per row, not the raw column).
  - **No pause/resume** — deliberately removed; deleting is the only lifecycle action exposed to the owner besides creating. `IsActive` still exists on the model purely for the auto-deactivation cases above.
  - **Creating a series** (`pages/admin/RecurringAppointmentsPage.tsx`): the owner picks an item, a customer (`CustomerPicker`), then a **day-of-week button** (Sun–Sat, not a raw date picker), then a **time slot from the real availability grid** for the nearest upcoming date on that weekday (same `GET /api/admin/appointments/availability` endpoint the one-off modal uses) — never a free-typed time. That computed date becomes the series' `StartDate`.
  - `GET /api/cron/generate-recurring` (`CronController`) is the production trigger — same `CronSecret` bearer-auth pattern as `/api/cron/reminders` — meant to run once daily via an external scheduler.

### Database
EF Core + Npgsql. Dev DB: `barbersaas_dev` (appsettings.Development.json). Prod DB: `barbersaas` (appsettings.json). Auto-migrates in Development on startup.  
All times stored as `"HH:MM"` strings — zero-padded so string comparison is safe.  
Migrations run roughly: `InitialCreate` ... `AddPlatformAdminAndActivityLog` ... `AddWhatsAppBookingFlow`
... `AddBusinessTypeAndModel` → `RenameBarberToBusiness` → `GeneralizeServiceToItem` (the
multi-vertical rename) → `AddBusinessOwnerRequests` → `AddReviews` (adds `Reviews` +
`Businesses.RatingCount`/`RatingAverage`) → `AddBusinessDiscoveryFields` (adds
`Businesses.City`/`AddressLine`/`MapUrl`/`IsListed` + indexes) → `AddCustomerOtp` (adds the
`CustomerOtps` table for phone+OTP login) — the latest as of this writing. Production does **not** auto-migrate (`db.Database.Migrate()` runs only when
`IsDevelopment()`); a migration-bearing deploy must apply it via the Railway Postgres tunnel
**before** the code push, not after (see [Deployment (Railway)](#deployment-railway)) — skipping
that ordering has caused multiple prod outages.

### Availability Engine
`Services/AvailabilityService.cs` — generates 30-min slots between working hours start/end, then removes any slot that overlaps with: breaks, blocked slots, or existing CONFIRMED appointments. Also drops slots where `startTime + itemDuration > workingHours.EndTime`. For **today's date specifically**, also drops any slot whose start time is at or before the current time — a customer booking at 15:00 can't grab a 10:00 slot. `WorkingHours`/`Appointment` start/end times (`"09:00"`, `"17:30"`, ...) are the business's local wall-clock hours and are never converted to/from UTC anywhere in this app, so "now" is taken as `DateTime.Now` (local server time), not `DateTime.UtcNow` — comparing against UTC would be off by the server's UTC offset (this was a real bug: a customer could book a slot that had already passed).

### Appointment status: no manual "Complete"
The business can only cancel an appointment now (`AdminController.UpdateAppointmentStatus` rejects any `status` other than `CANCELLED`) — there's no "Mark Complete" button anywhere in the admin UI. Instead, `Services/AppointmentStatusHelper.EffectiveStatus(status, date, endTime)` computes "COMPLETED" automatically for any still-`CONFIRMED` appointment whose end time has passed (compared against `DateTime.Now`, local server time — same reasoning as the Availability Engine above), applied wherever a status is returned to a client: `AdminController` (dashboard + appointments list), `CustomerAppointmentsController.GetMyAppointments`, and `BookingController.GetAppointment` (the magic-link view). `CANCELLED` is never overridden. The stored `AppointmentStatus` column itself stays `CONFIRMED` — only the API response's status string is computed; nothing rewrites the DB row.

### WhatsApp chatbot: self-hosted via Baileys (whatsapp-bridge)
As of 2026-09-18, the WhatsApp chatbot's transport is a **self-hosted Baileys session per business**
(`whatsapp-bridge/`, a separate Node.js service), not Twilio's official WhatsApp Business API. Each
business owner links their own regular WhatsApp number by scanning a QR code (like WhatsApp
Web/multi-device linking) — no Meta Business Manager, no Trust Hub, no registration needed. This
replaced Twilio because Twilio's WhatsApp Business API requires Meta Business verification (Trust
Hub), which was **rejected 2026-09-15** for this unregistered personal project (no business
registry entry, matching domain, or verifiable public presence) — a wall that applies once per
Twilio account, so it blocks *any* real number, and switching BSPs doesn't help since it's a Meta
requirement, not a Twilio one. **Known, accepted tradeoff**: this uses WhatsApp outside its official
Business API and violates WhatsApp's Terms of Service — a linked number can be banned by Meta's
automated detection with no appeal.

All booking/chatbot logic stays in this backend (`WhatsAppController`) — `whatsapp-bridge` is a thin
transport adapter: it holds one Baileys socket per linked business, forwards inbound messages to
`POST /api/whatsapp/bridge/inbound` (auth: shared secret header `X-Bridge-Secret`, config
`WhatsAppBridge:Secret` — same pattern as `CronSecret`, checked on both ends), and sends back
whatever `reply` comes back (`null` means send nothing, e.g. `ChatbotEnabled = false`). Outbound
sends (reminders, waitlist notifications, cancellation-approval requests) go through
`BridgeWhatsAppSender` (`IWhatsAppSender`, registered in `Program.cs`), which calls the bridge's
`POST /send` via `IWhatsAppBridgeClient`/`WhatsAppBridgeClient`. See `whatsapp-bridge/README.md` for
the service's own env vars and Railway deployment notes.

`Business.WhatsAppNumber` (renamed from `TwilioNumber`) is the linked number's display string —
written automatically once linking succeeds (`GetWhatsAppLinkStatus` below), never self-entered by
the business owner; their own Settings page shows it read-only. The platform admin drives linking
from `PlatformAdminBusinessDetailPage` ("Link WhatsApp" button → QR code → "Connected as +...")
via three endpoints that proxy the bridge:
- `POST /api/platform-admin/businesses/{id}/whatsapp/link` — starts a session, bridge generates a QR.
- `GET /api/platform-admin/businesses/{id}/whatsapp/status` — polled by the frontend every ~2s while
  linking; on `state: "connected"`, also writes the resolved phone number into `Business.WhatsAppNumber`.
- `DELETE /api/platform-admin/businesses/{id}/whatsapp/link` — unlinks and clears `Business.WhatsAppNumber`.

**Twilio's WhatsApp Business API integration is kept in the codebase, unregistered, as a dormant
fallback** (`TwilioWhatsAppSender`, and `WhatsAppController`'s original `POST /api/whatsapp/webhook`
with its Twilio-signature validation) — in case a real registered business + Trust Hub approval ever
happens later. Nothing in production currently points at it (no business has a real Twilio WhatsApp
number). `Twilio:AccountSid`/`Twilio:AuthToken` config still exists for this dormant path and for
`TwilioOtpSender` (phone+OTP customer login, a separate Twilio product — Programmable SMS, unrelated
to WhatsApp).
- `WhatsAppController.ProcessMessageAsync` is the shared dispatcher for both the bridge-inbound and
  legacy Twilio paths — see the AI layer subsection below for what it dispatches to.
- Reminders are sent by hitting `/api/cron/reminders` (e.g. via an external cron job or scheduler).
- **Lockout after repeated invalid replies** (rule-based path only): 3 consecutive non-numeric/
  out-of-range replies to a "which service?" prompt (`WhatsAppConversationState.InvalidAttempts`,
  `WhatsAppController.MaxInvalidAttempts`) stops the bot replying to anything at all -- including
  cancel/reschedule keywords -- until the customer sends the literal unlock keyword (`"$"`,
  `WhatsAppController.UnlockKeyword`), which restarts the conversation from the opening prompt.
  Prevents the bot replying forever to someone sending random text.
- **Quiet while a booking link is pending** (rule-based path only): once a booking link is issued
  (`WhatsAppController.IssueBookingLink`), the conversation-state row is kept alive (not removed)
  with `AwaitingBookingCompletion = true` instead, valid for the same 24h the link itself is --
  any further message from that phone gets no automated reply at all, rather than re-sending the
  opening prompt, while the customer finishes booking on the web page the link opened.
  `BookingController.BookAppointment` clears the flag once the appointment is actually created, and
  -- whenever `Business.WhatsAppNumber` is set, regardless of whether the booking came from
  WhatsApp at all -- sends the customer a `whatsapp.bookingConfirmed` confirmation message (service,
  date, time) via `IWhatsAppSender`, best-effort (failures are logged, never block the booking).

### WhatsApp chatbot: optional OpenAI layer
As of 2026-09-18, `WhatsAppController.ProcessMessageAsync` tries an LLM-driven path
(`ProcessMessageWithAiAsync`) before falling back to the original rule-based one
(`ProcessMessageRuleBasedAsync`, unchanged keyword/numeric-reply logic) — active only when
`OpenAI:ApiKey` is configured (secret, no default — same pattern as `Twilio:AuthToken`/`CronSecret`;
`OpenAI:Model` optionally overrides the default model), and falls back to the rule-based path for
that message on *any* exception from the AI call, so an OpenAI outage/rate-limit never breaks the
bot. This lets the bot understand free-form messages ("actually can we move it to Thursday")
instead of only exact keywords/numbers.

**Guardrail — the model never composes text for a completed action itself.** It only decides *when*
to call one of two tools (`create_booking_link`, `cancel_upcoming_appointment`); the tool
implementations build the exact same `I18nService`-templated text the rule-based path already uses
(shared via `IssueBookingLink`/`FindAndCancelUpcomingAppointment`), and the system prompt instructs
the model to relay a tool's `message` field verbatim rather than reword it. This is what prevents a
hallucinated URL, price, or date reaching a customer — the model only freely composes text for
open-ended Q&A (hours, prices, greetings) grounded in the business data (name, active items,
language) injected into its system prompt each turn. Rescheduling isn't a separate tool — the
system prompt tells the model to call `cancel_upcoming_appointment` then `create_booking_link`.

`WhatsAppConversationState.HistoryJson` (nullable, added alongside this) holds the rolling chat
history (last ~12 turns, `List<OpenAiTurn>` JSON) for this path — the same (BusinessId, Phone) +
`ExpiresAt` row the rule-based path already used for its "awaiting numbered reply" flag, repurposed
rather than adding a new table. The two paths' state semantics are kept deliberately separate even
though they share the row: the rule-based path clears the whole row on cancel (`ClearConversationState`);
the AI path's tool executors (`ExecuteCreateBookingLink`/`ExecuteCancelUpcomingAppointment`) call the
lower-level DB helpers directly and never touch/clear that row, so AI conversation history survives
a cancellation instead of being wiped.

`Services/IOpenAiChatClient.cs`/`OpenAiChatClient.cs` wraps the official `OpenAI` NuGet package
(Chat Completions with tool calling) — pure transport, no DB access, mirroring
`IWhatsAppBridgeClient`. Not business-toggleable per business — a single platform-wide switch via
config presence, matching the `IEmailSender` precedence-chain pattern.

### Chatbot customization & language auto-detection
Per business, in `Settings > WhatsApp Chatbot`:
- `Business.ChatbotEnabled` (default `true`) — when off, `WhatsAppController.Webhook` returns an
  empty `<Response></Response>` TwiML body (no automated reply at all) for every inbound message,
  cancel/reschedule keywords included — the business wants to answer customers themselves.
- `Business.ChatbotWelcomeMessage` / `ChatbotConfirmationMessage` (both nullable free text, one
  language each, not per-EN/AR/HE) — when set, replace the *default* greeting/confirmation text
  only; the item list and its surrounding instructions always stay in the detected language
  (see below), so a custom welcome message is followed by `whatsapp.selectServicePrompt`, not the
  full `whatsapp.selectService` template. A custom confirmation message may include a literal
  `{url}` placeholder to control where the booking link lands in the text; if omitted, the link is
  appended on its own line.

**Language auto-detection** (`WhatsAppController.DetectLanguage`): every inbound message's script
is checked against the Hebrew (`U+0590`–`U+05FF`) and Arabic (`U+0600`–`U+06FF`) Unicode blocks,
falling back to `EN` if it has any Latin letters at all. This is independent of the business's own
configured storefront `Language` — the bot always replies in whatever language the *customer* just
typed in. A message with no letters at all (a bare numeric reply like `"1"`) carries no signal of
its own, so `ResolveLanguage` falls back to the language already stored on the open
`WhatsAppConversationState` row (see below) for that phone, and only falls back to the business's own
default when there's no open conversation either (a signal-less first message, e.g. an emoji).
`WhatsAppConversationState.Language` and `WhatsAppBookingToken.Language` both persist the resolved
language — the latter is returned by `POST /api/customer/auth/whatsapp` (`language` field) and the
frontend's `loginWithWhatsAppToken` calls `setLang()` with it, so the booking wizard opens in the
same language the customer was just chatting in, not whatever was last stored in this browser.

**Arabic-Indic numeral replies** (`WhatsAppController.NormalizeDigits`): a numbered-selection reply
in Arabic-Indic (`٠`-`٩`) or Extended Arabic-Indic/Persian (`۰`-`۹`) digits is translated to ASCII
before `int.TryParse` in `TryHandleServiceSelectionReply` — a customer replying in Arabic script
naturally types the number in one of these, not by switching to a Western keyboard.

### Customer login via WhatsApp
The primary way a customer session starts: redeeming a link the WhatsApp bot sent them (no
sign-up/sign-in step at all). Phone+OTP (below) is a second, parallel path — both just mint the
same kind of `CustomerAccount`-backed JWT. Flow (`WhatsAppController` + `CustomerAuthController`):
1. Any message from a phone with no pending selection (or the `book` keyword) gets a numbered list
   of the business's active items (`whatsapp.selectService`, item order = `Item.Id` order)
   and opens a `WhatsAppConversationState` row (`BusinessId`+`Phone`, 10-minute expiry) remembering
   the bot is waiting on a reply — Twilio webhooks are stateless per-message, so this is the only
   way to connect the "which item?" prompt to the customer's numeric reply that follows.
2. A valid numeric reply creates a `WhatsAppBookingToken` (`WhatsAppBookingTokenService.CreateAsync`)
   — an **opaque, DB-backed** id, not a JWT, so the phone number it carries can't be read off the
   URL — and replies with `{AppUrl}/{slug}/w/{token}`. Reusable for 24h (no one-time-use flag), so
   reopening the WhatsApp message later the same day still works. An invalid reply reprompts and
   keeps the state row; the `cancel`/`reschedule` keywords clear it.
3. Opening that URL (`WhatsAppLandingPage`) calls `POST /api/customer/auth/whatsapp` with the
   token, which resolves it (400 if missing/expired, 404 if the business/item was deleted or
   deactivated since), upserts a `CustomerAccount` by phone — splitting Twilio's `ProfileName` form
   field (the sender's WhatsApp display name) into `Name`/`FamilyName` on the first space, falling
   back to a generic name if WhatsApp didn't supply one — and returns a normal customer JWT via
   `CustomerJwtService.Generate` (same call the phone+OTP verify action below uses) plus
   `{ businessSlug, itemId }`. The frontend then redirects into `/:slug/book?serviceId=`, which
   skips straight to date selection (`BookingWizard`'s deep-link `useEffect`) — no sign-up/sign-in
   step, no item list to pick from again.

`PhoneNormalizer.Normalize` (used everywhere phones are stored/matched) keeps a bare local number
as-is if the customer didn't type a `+` — WhatsApp's `From` field always arrives in E.164 already,
so this mainly matters for matching against phones entered elsewhere (owner-created appointments,
the booking form's editable phone field).

### Customer login via phone+OTP
A second, parallel entry point alongside WhatsApp login (`CustomerAuthController`, re-added
2026-09-14 — it existed before the WhatsApp flow replaced it as the *only* path on 2026-09-04, then
came back as an *additional* one). It exists mainly because a future native mobile app (see the
product spec's "Future Expansion: Mobile application") can't rely on "the customer already
messaged us on WhatsApp" as its only sign-in — a mobile client needs a normal, direct login it can
drive itself. The API is plain JSON-in/JWT-out already (no cookies, no web-only assumptions), so
it needs no backend changes to be consumed by a mobile client later.

Flow: `POST /api/customer/auth/otp` `{ phone }` generates a 6-digit code (bcrypt-hashed in
`CustomerOtp`, 10-minute expiry, 45s cooldown + 5/hour cap — same limits as `AuthController`'s
business-side email codes), sends it via `IOtpSender` (`TwilioOtpSender` over Twilio's
**Programmable SMS** API — not WhatsApp, since no business is identified yet at this point — when
`Twilio:FromNumber` is configured, else `DevOtpSender` no-ops and the code comes back as `devOtp`
in Development, matching `AuthController`'s `devCode` convention). A fixed test phone/code pair
(`0501234567` / `123456`) always succeeds without sending a real SMS, for app-store reviewer
logins once the mobile app exists. `POST /api/customer/auth/verify` `{ phone, otp, name?,
familyName? }` checks the code (attempts/expiry enforced), upserts `CustomerAccount` (name/
familyName required only for a brand-new account), backfills any pre-existing guest-booked
`Customer.CustomerAccountId` row for that phone (same `ExecuteUpdateAsync` pattern the WhatsApp
login uses), and returns the same JWT shape as WhatsApp login.

Frontend: `pages/public/CustomerLoginPage.tsx` (`/login`, phone → OTP two-step form, `?next=`
target, dev-code banner) and `lib/customerAuth.tsx`'s `requestOtp`/`verifyOtp` (alongside
`loginWithWhatsAppToken` — all three end at the same shared `storeSession` helper). Linked from
`CustomerProtectedRoute`'s unauthenticated fallback and `CustomerAccountNav`'s logged-out state.

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
```

`Jwt:Secret`, `CronSecret`, `Twilio:AccountSid`/`Twilio:AuthToken`, `Twilio:FromNumber`, `WhatsAppBridge:Secret`, and `OpenAI:ApiKey` are **not** in `appsettings.json` — there's no default, so the app fails fast (or, for the Twilio/bridge values, simply can't validate/send) if they're missing rather than silently falling back to a guessable value. `Twilio:FromNumber` is a platform-level SMS-capable Twilio number for the phone+OTP customer login code (`TwilioOtpSender`) — separate from WhatsApp entirely. `Twilio:AccountSid`/`Twilio:AuthToken` back the dormant legacy WhatsApp path only (see [WhatsApp chatbot: self-hosted via Baileys](#whatsapp-chatbot-self-hosted-via-baileys-whatsapp-bridge)) — nothing in production currently uses them for WhatsApp.
- **Local dev**: stored in the `dotnet user-secrets` store for `backend/BarberSaas.Api.csproj` (`UserSecretsId` in the `.csproj`, values live outside the repo at `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`). `dotnet run` loads them automatically in Development.
- **Production**: supply via environment variables (`Jwt__Secret`, `CronSecret`, `Twilio__AccountSid`, `Twilio__AuthToken`, `Twilio__FromNumber`, `WhatsAppBridge__Url`, `WhatsAppBridge__Secret`, `OpenAI__ApiKey`, `OpenAI__Model`) or `appsettings.Production.json` (gitignored) — never commit real values.
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
**`whatsapp-bridge` is not yet deployed to Railway** (built 2026-09-18, still only run locally) — when
it is, it needs its own service (root dir `whatsapp-bridge`), a **Volume** mounted where
`SESSIONS_DIR` points (or every redeploy forces every business to re-scan their WhatsApp QR code),
and `BACKEND_URL` pointed at the backend's Railway **private** URL — see `whatsapp-bridge/README.md`.
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
