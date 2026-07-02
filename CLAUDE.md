# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Stack

**Backend**: `backend/` — ASP.NET Core 9 Web API (C#) + Entity Framework Core 9 + PostgreSQL  
**Frontend**: `frontend/` — Vite 5 + React 18 + TypeScript SPA

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

## Project Structure

```
barber-saas/
├── backend/
│   ├── Controllers/
│   │   ├── AuthController.cs       # POST /api/auth/register|login
│   │   ├── AdminController.cs      # Protected admin CRUD (JWT required)
│   │   ├── BookingController.cs    # Public booking API (no auth)
│   │   ├── WhatsAppController.cs   # Twilio webhook — replies to customer messages
│   │   └── CronController.cs       # GET /api/cron/reminders — sends 24h WhatsApp reminders
│   ├── Data/AppDbContext.cs         # EF Core DbContext, indexes, relationships
│   ├── DTOs/AuthDtos.cs             # All request/response record types
│   ├── Migrations/                  # EF migrations (InitialCreate applied)
│   ├── Models/Barber.cs             # All entity classes (Barber, Service, Appointment, etc.)
│   ├── Services/
│   │   ├── AvailabilityService.cs  # Slot generation + conflict filtering
│   │   ├── I18nService.cs          # Server-side translations (EN/AR/HE) for WhatsApp messages
│   │   └── JwtService.cs           # JWT generation (30-day tokens, HS256)
│   ├── Program.cs                   # App startup, DI registration, middleware pipeline
│   ├── appsettings.json             # Base config (prod DB, JWT keys, AppUrl, CronSecret)
│   ├── appsettings.Development.json # Dev overrides (DB = barbersaas_dev, verbose logging)
│   └── Properties/launchSettings.json  # Port 5280, ASPNETCORE_ENVIRONMENT=Development
└── frontend/
    └── src/
        ├── components/
        │   ├── admin/        # AdminLayout, AdminSidebar, WeeklyCalendar
        │   └── booking/      # BookingWizard (5-step)
        ├── lib/
        │   ├── api.ts        # Axios instance — baseURL /api, JWT request interceptor, 401 auto-logout
        │   ├── auth.tsx      # AuthContext + AuthProvider + useAuth hook
        │   └── i18n.ts       # Client-side translations (EN/AR/HE) + t() + serviceName()
        └── pages/
            ├── admin/        # LoginPage, RegisterPage, DashboardPage,
            │                 #   AppointmentsPage, ServicesPage, SchedulePage, SettingsPage
            └── public/       # BarberPage, BookPage, AppointmentPage
```

## Architecture

Multi-tenant SaaS. Each barber is a **tenant** identified by a URL slug.

### Backend API Routes

**Auth (no JWT)**
- `POST /api/auth/register` — create barber account; auto-creates Mon–Fri 09:00–18:00 working hours
- `POST /api/auth/login` — returns JWT token (30 days)

**Admin (JWT required — barber ID read from token claims, never from body)**
- `GET/PATCH /api/admin/settings` — barber profile + Twilio config
- `GET/POST /api/admin/services` — services CRUD
- `PATCH/DELETE /api/admin/services/{id}` — update / soft-delete (IsActive = false)
- `GET/POST /api/admin/schedule` — working hours (upsert by DayOfWeek)
- `POST/DELETE /api/admin/schedule/breaks/{id}` — recurring breaks
- `POST/DELETE /api/admin/schedule/blocked/{id}` — one-off blocked dates/slots
- `GET /api/admin/dashboard?week=0` — weekly appointments (week offset from current)
- `GET /api/admin/appointments?filter=today|upcoming|past` — paginated appointment list
- `PATCH /api/admin/appointments/{id}` — update status (CONFIRMED/COMPLETED/CANCELLED)

**Public (no JWT)**
- `GET /api/{slug}/info` — barber name, services, active days, isRTL flag
- `GET /api/{slug}/availability?date=&serviceId=` — available 30-min slots
- `POST /api/{slug}/appointments` — book appointment; returns `{ appointmentId, cancelToken }`
- `GET /api/{slug}/appointments/{id}` — view appointment details
- `DELETE /api/{slug}/appointments/{id}?token=` — cancel (validated by cancelToken)
- `PATCH /api/{slug}/appointments/{id}?token=` — reschedule (re-checks availability first)

**Integrations**
- `POST /api/whatsapp/webhook` — Twilio webhook; validates X-Twilio-Signature; replies in barber's language to book/cancel/reschedule keywords
- `GET /api/cron/reminders` — send 24h WhatsApp reminders; requires `Authorization: Bearer <CronSecret>`

### Frontend Routes
- `/` — landing/marketing page
- `/admin/login`, `/admin/register` — auth pages
- `/admin/dashboard` — weekly calendar view
- `/admin/appointments` — appointments table with filters
- `/admin/schedule` — working hours, breaks, blocked dates
- `/admin/services` — services CRUD
- `/admin/settings` — business info, Twilio setup
- `/:slug` — public barber page
- `/:slug/book` — 5-step booking wizard
- `/:slug/appointments/:id?token=<cancelToken>` — view/cancel/reschedule appointment

### Auth
JWT Bearer token stored in `localStorage`. `api.ts` adds it automatically via request interceptor. 401 responses redirect to `/admin/login`. Admin routes are wrapped in `ProtectedRoute` which checks `useAuth().isAuthenticated`.

### i18n (Translations)
- **Frontend**: `frontend/src/lib/i18n.ts` — typed `const` object with EN/AR/HE strings.  
  Use `t(lang, 'key')` for UI strings and `serviceName(service, lang)` for multilingual service names.  
  All public pages (BarberPage, BookPage, AppointmentPage, BookingWizard) use this.  
  RTL layout applied via `dir="rtl"` when `barber.isRTL` is true (AR and HE).
- **Backend**: `backend/Services/I18nService.cs` — static `T(lang, key, args)` for WhatsApp/reminder messages.

### Database
EF Core + Npgsql. Dev DB: `barbersaas_dev` (appsettings.Development.json). Prod DB: `barbersaas` (appsettings.json). Auto-migrates in Development on startup.  
All times stored as `"HH:MM"` strings — zero-padded so string comparison is safe.  
Migration: `InitialCreate` already applied.

### Availability Engine
`Services/AvailabilityService.cs` — generates 30-min slots between working hours start/end, then removes any slot that overlaps with: breaks, blocked slots, or existing CONFIRMED appointments. Also drops slots where `startTime + serviceDuration > workingHours.EndTime`.

### Twilio / WhatsApp
Twilio credentials are stored **per-barber** in the `Barbers` table (`TwilioSid`, `TwilioToken`, `TwilioNumber`) — not in appsettings. Barbers configure these in their Settings page.  
- Webhook replies to EN/AR/HE keywords (book, cancel, reschedule) with booking links or cancels the next upcoming appointment directly.  
- Reminders are sent by hitting `/api/cron/reminders` (e.g. via an external cron job or scheduler).

### Configuration (`backend/appsettings.json`)
```
ConnectionStrings:Default   PostgreSQL connection string (prod: barbersaas)
Jwt:Issuer                  barbersaas-api
Jwt:Audience                barbersaas-frontend
AppUrl                      Public frontend URL (used in WhatsApp message links)
AllowedOrigin               CORS allowed origin (frontend URL)
```

`Jwt:Secret` and `CronSecret` are **not** in `appsettings.json` — there's no default, so the app fails fast if they're missing rather than silently falling back to a guessable value.
- **Local dev**: stored in the `dotnet user-secrets` store for `backend/BarberSaas.Api.csproj` (`UserSecretsId` in the `.csproj`, values live outside the repo at `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json`). `dotnet run` loads them automatically in Development.
- **Production**: supply both via environment variables (`Jwt__Secret`, `CronSecret`) or `appsettings.Production.json` (gitignored) — never commit real values.
- Rotating either secret invalidates all existing JWTs/cron callers signed with the old value — expected, not a bug.
