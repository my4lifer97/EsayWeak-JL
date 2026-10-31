---
name: platform-admin-and-onboarding
description: barber-saas platform admin panel, owner-email composer and Gmail health alert, business-owner onboarding (self-service and admin-approved), multi-vertical business types, reviews, public directory. Use when touching those areas.
---

<!-- Moved out of CLAUDE.md (2026-10-25) so it loads only when needed. -->

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

`BusinessesController.Search` is the directory: base filter `IsListed` +
`Business.AcceptsBookingsAt`, combinable `query` / `businessTypeKey` / `city` filters, `sort` of `rating` (default) /
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
2. **Admin-approved** — a prospective owner first proves they control the email address they typed
   (`POST /api/business-owner-requests/send-email-code` `{ email }` -- 6-digit code, `BusinessOwnerRequestEmailOtp`,
   same 45s cooldown/5-per-hour/10-min-expiry limits as `AuthController`'s self-service codes, sent
   via the platform admin's own Gmail (`IOwnerEmailSender`, see below) rather than the system
   `IEmailSender` chain, so it visibly comes from a real account), then submits
   `POST /api/business-owner-requests` (`BusinessOwnerRequestsController`, public) with that code
   plus their name, business name/type, and contact info -- 400 if the code doesn't match an
   unconsumed, unexpired row for that email. A `BusinessOwnerRequest` row (`Pending`/`Approved`/`Rejected`)
   is created and a platform admin reviews it at `/platform-admin/requests`. Approving
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
`Action`, `Description`, method/path/status/IP/device (`UserAgent`), and `ImpersonatedByPlatformAdminId`
when acting via an impersonation token) — deliberately request metadata only, never request bodies,
so passwords or Twilio tokens can never end up in a log row. A business owner **signing in** is
logged explicitly too (`AuthController.LogLoginAsync`, called from `Login`/`VerifyEmail`/
`ResetPassword`) since login itself is anonymous and the generic filter never sees it (no identity
on the *incoming* request, even though the response carries a fresh token). `/platform-admin/businesses/:id`
and `/customers/:id` render that entity's activity feed via `ActivityLogTable`, whose rows expand
on click to show the full action/path/IP/device.

#### Owner-email composer
`/platform-admin/businesses/:id`'s "Email owner" button and the business-owner-request approval
flow both open `OwnerEmailComposer` (`components/platform-admin/`) — lets the admin check any
combination of username / a freshly generated temporary password / a freshly generated WhatsApp
shareable link (see below) to include, edit the exact subject/body live before anything is sent,
then either send it for real, copy it to paste into their own mail client, or just read it off
screen during a phone call. `POST /api/platform-admin/businesses/{id}/email` sends whatever
subject/body the frontend composed — no server-side templating, so the preview is exactly what
goes out. Sent via `IOwnerEmailSender` (`GmailApiEmailSender`), **not** the system `IEmailSender`
chain used for verification/forgot-password codes: the Gmail API is a plain HTTPS call
authenticated via OAuth2 against the platform admin's own Gmail account, so (like self-hosting
WhatsApp via Baileys instead of a paid Business API) it sidesteps Railway's outbound SMTP port
block instead of relying on a third-party relay (Brevo) the recipient has never heard of. See the
`Gmail:*` config keys below for the one-time OAuth setup.

**Paid period** (added 2026-10-31, for owners who pay the admin directly while Cardcom is off):
`Business.PaidUntil` is the last paid day (`DateOnly`, Israeli date, inclusive), set on the platform-admin
business page. Saving a date makes the business `ACTIVE` (past dates are refused); clearing it returns it
to `TRIAL` unless it has a `CardcomToken`. The owner's Settings shows "active until {date}", then a red
"paid period ended" once it passes. **Passing the date only tells the admin, it never stops the business:**
`PaidPeriodWorker` runs `PaidPeriodMonitor` hourly, which emails one alert per business through
`PlatformAdminAlerts` (Brevo, so a broken Gmail doesn't matter) the day after `PaidUntil`.
`PaidUntilAlertSentFor` records the date alerted, so a new date re-arms it; a failed send retries next run,
and disabled businesses are skipped. `PaidPeriodEndedBanner` on the dashboard lists every ended, not-disabled
business until it's renewed or disabled. Worker off in tests (`PaidPeriod__WorkerEnabled=false`).

**Gmail health alert** (added 2026-10-11, after the refresh token expired silently for days):
`OwnerEmailHealth` (in-memory singleton) tracks whether the Gmail channel works. `GmailApiEmailSender`
reports every send into it (a 400 from the send call is ignored, since that's one bad recipient, not
a broken channel), and `OwnerEmailHealthWorker` probes the token exchange without sending ~30s after
startup and then every `OwnerEmailHealth:CheckIntervalHours` (default 6). When it's failing,
`OwnerEmailMonitor` emails the platform admin through the **system `IEmailSender` chain (Brevo)**, not
Gmail, since the broken channel can't report itself: one alert per episode, repeated every
`OwnerEmailHealth:AlertRepeatHours` (default 24), plus one "working again" email after recovery. If the alert email itself fails, it's retried after 15 minutes, not every tick.
Recipient is `Platform:AdminNotificationEmail`, else every `PlatformAdmin.Email`. The platform-admin
dashboard shows `OwnerEmailHealthBanner` from `GET /api/platform-admin/system-health`, with a "Check
again" button (`POST .../system-health/owner-email/check`). The worker is off in tests
(`OwnerEmailHealth__WorkerEnabled=false`); not persisted, so status is "unknown" until the first probe
after a restart.

`POST .../whatsapp/link-token` (WhatsApp shareable link, see
[WhatsApp chatbot](#whatsapp-chatbot-self-hosted-via-baileys-whatsapp-bridge)) is called from inside
the composer to generate a value for the preview. **A temporary password is never applied just by
generating it** (fixed 2026-10-11 — it used to call `reset-password`, so closing the composer or a
failed Gmail send left the owner locked out with a password nobody received):
`POST .../generate-temp-password` only returns a value; `POST .../email` takes it as
`newTempPassword` (must appear in the body, else 400) and sets it + `MustChangePassword` only
**after** the send succeeds (502 "password was NOT changed" otherwise). For copying it or reading it
out on a call, the composer's "Set password now" (and "Copy to Clipboard" when the copied email includes a not-yet-active password, after asking) calls `reset-password` with
`{ temporary: true, newPassword, silent: true }`. `Silent` (default `false`) skips reset-password's
own credentials email (owner Gmail sender); `POST .../business-owner-requests/{id}/approve` has the same `Silent` flag — the
composer opens right after approval, pre-filled with the new username and the (already applied)
temp password.

**Guards (2026-10-12):** the server keeps only a hash, so closing the composer while an *applied*
password was never sent or copied asks "Close anyway?" first, since otherwise it's gone for good.
Only text that actually contains the password counts (QA run 4): copying with Password unticked, or
with a template that has no `{{fields}}`, still asks; and Copy refuses to *set* a new password that
the copied text wouldn't include (Send already did, server-side).
Whenever a username or password is included, the field lines also add `Sign in at: {origin}/admin/login`.
Every irreversible platform-admin action asks first via `useConfirmDialog('EN')`: approve, reject,
disable (not re-enable), WhatsApp unlink, both password resets and the composer's "Set password now".
