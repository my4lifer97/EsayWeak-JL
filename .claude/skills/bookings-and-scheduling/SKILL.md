---
name: bookings-and-scheduling
description: barber-saas booking rules: availability engine, overlap lock, per-customer limits, waitlist and cancellation approval, owner-created and recurring appointments, computed COMPLETED status, item photos, booking-wizard back navigation. Use when touching booking, appointments, schedule or waitlist code.
---

<!-- Moved out of CLAUDE.md (2026-10-25) so it loads only when needed. -->

### Per-customer booking limits
A business can cap how many times the *same customer* (matched by phone, not account — so a second
login for the same number, e.g. phone OTP next to the WhatsApp link, still counts) can book with them:
`Business.MaxBookingsPerDay` / `MaxBookingsPerWeek` (nullable int, `null` = unlimited), set via
`Settings > Booking Limits`. Enforced in `BookingController.BookAppointment` before creating the
appointment — "per week" means the fixed Sun–Sat calendar week containing the requested date.
A customer's reschedule (magic link or My Bookings) is checked too, not counting the appointment
being moved (2026-10-20); the owner's own reschedule isn't.

### No overlapping bookings (advisory lock)
Every booking/reschedule write (public, customer, owner) saves through
`AvailabilityService.TrySaveOrDetectConflict`, which on Postgres takes
`pg_advisory_xact_lock(hashtext('booking:{businessId}:{date}'))`, re-checks for any overlapping
CONFIRMED appointment (excluding the rows being saved, so a reschedule doesn't clash with its own
old time), then saves and commits. The earlier `GetAvailableSlots` check alone let two concurrent
requests with different-but-overlapping start times both through; the partial unique index on
`(BusinessId, Date, StartTime)` only stops identical starts and stays as a backstop. SQLite tests
skip the lock (single writer).

### Returning customer's name stays editable, remembered from last time
`BookingWizard`'s First/Family Name fields are always editable (only the phone field is `disabled`
when authenticated) — a correction the customer types on any booking is saved
(`BookingController.BookAppointment` always writes `customerName`/`customerFamilyName` onto the
(business, phone) `Customer` row, whether it's brand new or already existed) and becomes what's
remembered next time.

**"Remembered next time" is business-specific, not the account's own name.** `CustomerAccount.Name`/
`FamilyName` is only ever auto-filled from a WhatsApp profile name once, on the account's
first-ever creation (`CustomerAuthController.LoginWithWhatsApp`), and never updated after that —
relying on it alone would mean a correction typed while booking with one business never carries
over to that business's next WhatsApp link (and a WhatsApp display name with no second word, e.g.
just "Luna", would leave `FamilyName` permanently empty). So `LoginWithWhatsApp`'s response prefers
the existing per-business `Customer.Name`/`FamilyName` for that (business, phone) when one exists,
falling back to the account's own name only for a true first-time-with-this-business customer.

### Back-navigation floor once a customer arrives via a WhatsApp link
`BookingWizard` reads `?itemId=` (set by both `WhatsAppLandingPage`'s redirect and a waitlist
notification's deep link) once on mount into `isFromLink` — when true, the item was already chosen
in the WhatsApp chat, so this session can't get back to item selection (step 1) or leave the wizard
page entirely: a `popstate` listener re-pushes the current history entry as a best-effort trap
against the browser's own back button (this can't fully override the OS/browser back gesture, only
discourage it — there's no web API that can), and the `BackButton`/leave-page control never renders
for this flow. Date selection (step 2) is the floor the customer lands on and can't go back past.
From step 3 (time) or step 4 (details/confirm) onward, though, the normal in-page "← Back" control
*does* render and steps them back one at a time (time → date, confirm → time) — only reconsidering
the already-chosen item is blocked, not the date/time/details picked on this page. A customer who
opened the site directly (no `itemId` param, always starts at step 1) is unaffected and keeps normal
back navigation at every step.

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
Uploads reuse the same JPG/PNG/WEBP/5MB validation as the business's own logo upload (`Services/ImageUpload.ValidateAsync`, which also checks the file's first bytes match its extension, so a renamed HTML file is rejected). The logo gets a new file name on every upload (`{businessId}-{guid}{ext}`, old file deleted) so caches can't keep showing the old one;
gallery photos live under `wwwroot/uploads/gallery/{itemId}/`, customer-uploaded reference
photos under `wwwroot/uploads/appointment-photos/` — both served via the existing `/api/uploads`
static file route.

### Waitlist & cancellation approval
Two related, independently-toggleable `Business` settings (`Settings > Booking Limits` area):

- **`WaitlistEnabled`** — lets a customer `POST /api/{slug}/waitlist/{appointmentId}` to join the
  waitlist for a slot that's currently booked (`WaitlistEntry`, idempotent per customer+appointment).
  `WaitlistService` notifies waiting customers (by their configured status, `WAITING` →
  `NOTIFIED`/`RESOLVED`) when `AppointmentCancellationService.CancelAsync(notifyWaitlist: true)`
  frees that slot — **one at a time, in join order** (`CreatedAt`): the first is messaged right
  away, and each `Waitlist:NotifyIntervalMinutes` (default 5) that the slot stays free, the next
  one is. A booking that **overlaps** the freed slot (`ResolveForRebooking`, not only an identical
  start) flips every remaining entry to `RESOLVED`, which ends the queue. Each send also re-checks
  that no CONFIRMED appointment overlaps the slot (`IsSlotTaken`) and ends the queue if one does,
  which covers paths that don't call `ResolveForRebooking`, such as recurring generation. The follow-up sends come from `Services/WaitlistQueueWorker.cs`, an
  in-process `BackgroundService` polling `WaitlistService.AdvanceQueues` every
  `Waitlist:WorkerPollSeconds` (default 30) — the 15-minute external cron is too coarse for a
  5-minute gap. Disabled in tests via `Waitlist__WorkerEnabled=false`. A slot whose start time
  already passed is never offered (`WaitlistService.HasStarted`, checked on the first in-request
  send too since QA run 4, which then closes the queue; the owner's "offer to waitlist" uses it as well). Each message is written in the language the WhatsApp
  **chatbot last replied to that customer in** (`CustomerAccount.LastMessageLanguage`, set by
  `WhatsAppController.RememberCustomerLanguage` from the resolved reply language on every inbound
  message the chatbot answers), falling back to the business's `ChatbotDefaultLanguage ?? Language`.
  - **Rescheduling frees the old slot for the waitlist** (all three paths: customer
    `PATCH /api/customer/appointments/{id}/reschedule`, magic-link `PATCH /api/{slug}/appointments/{id}`,
    owner `PATCH /api/admin/appointments/{id}/reschedule`). The appointment row itself moves (keeps
    its Id/CancelToken so the customer's links still work); if anyone is waiting on it,
    `WaitlistService.DetachForReschedule` leaves a `CANCELLED` copy at the old date/time, re-points
    the waiting entries to it, and the queue starts after the save succeeds. The copy has
    `IsWaitlistCarrier = true` and is left out of the customer's and the owner's appointment lists.
  - A customer can't join the waitlist for **their own** appointment (400). On the booking page
    (`BookingWizard`) their own booked slot shows as "Your appointment" and opens their
    `AppointmentCard` (note / reschedule / cancel) instead of the waitlist modal.
- **`RequireApprovalOnCustomerCancel`** — changes what happens when a *customer* cancels (magic-link,
  logged-in "My Bookings", or the WhatsApp `cancel` keyword — all three now route through
  `AppointmentCancellationService.CancelFromCustomerAsync` instead of flipping status directly).
  **Off** (default): cancels immediately, same as before, and auto-notifies the waitlist if enabled.
  **On**: the cancellation doesn't finalize — `Appointment.PendingCancellationApproval` is set true
  and `Status` deliberately **stays `CONFIRMED`** (no new status enum value), so the slot keeps
  blocking availability with zero changes needed to `AvailabilityService`; the business is instead
  asked and resolves it manually (offer to waitlist / cancel silently / replace customer) from the
  dashboard. The owner picks the channel(s) in Settings — `CancelApprovalNotifyViaWhatsApp`
  (default on) + `CancelApprovalWhatsAppNumber` and/or `CancelApprovalNotifyViaEmail` +
  `CancelApprovalEmail` (sent through the platform's Gmail via `IOwnerEmailSender`, the same sender as the platform-admin owner-credentials email — not the Brevo system chain); a blank contact falls back
  to `Business.Phone`/`Business.Email`, and the message is in `Business.Language`. Falls back to
  immediate-cancel automatically if no enabled channel can reach the owner (WhatsApp also needs the
  business's linked `WhatsAppNumber` to send from). `AppointmentStatusHelper.CustomerFacingStatus`
  returns `CANCELLATION_PENDING` for a frozen appointment (shown as "Cancellation requested"), not
  `CONFIRMED` and not yet `CANCELLED`. The owner's options for a request (`CancelOptionsModal`):
  **keep it** (`POST /api/admin/appointments/{id}/keep`, clears the flag), offer the slot to the
  waitlist, cancel silently, or **replace the customer** (`PATCH .../{id}/customer`). Replacing
  cancels the original row, which stays with the original customer and their magic-link token, and
  creates a new row for the new customer (its id is in the response); waiting entries move to the
  new row. It used to reassign the row, which made it vanish from the original customer's list and
  showed the new customer's details on the original customer's link. The claim goes through
  `TryClaimAsync`, so a customer reschedule that committed first makes the replace a 409.

### Owner-created & recurring appointments
Two ways for the business to book without the customer using the self-service flow:

- **One-off**: `AdminController.CreateAppointment` (`POST /api/admin/appointments`), triggered from the "New Appointment" button on `DashboardPage`/`AppointmentsPage` (`components/admin/NewAppointmentModal.tsx` + `CustomerPicker.tsx`). Same customer lookup by phone as public booking, except that a typed-in name that doesn't match the existing customer for that phone is a 409 (`Services/OwnerTypedCustomer`, also used by recurring create) instead of renaming them on every appointment; a matching name may fill in a blank family name. Also, `MaxBookingsPerDay/Week` is not enforced (that limit exists to stop *customers* gaming self-service booking). A `force: true` flag lets the owner book outside normal availability (walk-ins) by skipping the working-hours/breaks/blocked-slot check in `AvailabilityService.GetAvailableSlots`, while still hard-rejecting an exact double-booking via `AvailabilityService.HasConflictingAppointment`. Forcing still goes through `AvailabilityService.ForcedSlotError` (400): a real `HH:MM` time, ending by midnight, and not already in the past. Without it an owner could save "24:30" end times or a past-dated booking that counts as completed at once and unlocks a verified review. A forced recurring series checks `SlotTimeError` (time + midnight) on create and `ForcedSlotError` per generated occurrence.
- **Recurring**: `Models/Business.cs`'s `RecurringSeries` (business + customer + item + `DayOfWeek` + `StartTime`, no fixed end date in the UI though the model/DTO still accept an optional `EndDate`) plus `RecurringSkip` (append-only log of dates that couldn't be generated). `Appointment.RecurringSeriesId` (nullable FK, `SetNull` on series delete) links a generated occurrence back to its series — each occurrence is otherwise a fully normal, independently-cancelable `Appointment`; cancelling one has no effect on the series or its other occurrences.
  - **Generation**: `Services/RecurringAppointmentService.GenerateOccurrences()` walks each active series' `LastGeneratedThrough` cursor forward to a rolling horizon (default 8 weeks — `RecurringGeneration:HorizonWeeks`), creating an `Appointment` when `AvailabilityService.GetAvailableSlots` says the slot is free, or logging a `RecurringSkip` (`Reason = "slot_unavailable"`) when it isn't. Occurrences exist for the whole horizon (8 weeks) from the moment the series is created, so a later schedule change (a `BlockedSlot`, a preset closing that day) does meet already-generated rows: the Schedule page's conflict preview (below) lists them and can cancel them; dates beyond the horizon are generated later and skip a closed slot. The cursor never moves backward into the past, so resuming/creating after a gap doesn't backfill missed weeks. A series auto-deactivates (`IsActive = false`, logged as a `service_inactive` skip) if its linked `Item` is soft-deleted, or once its `EndDate` (if set) has passed.
  - **Immediate generation on create**: `RecurringAppointmentsController.Create` calls `RecurringAppointmentService.GenerateForSeriesNow(seriesId)` right after saving the series, so the first occurrence(s) exist and block their slot right away — otherwise nothing would appear on the dashboard, and the slot would stay bookable by others, until the next daily cron run. Both run in one transaction: a series none of whose weeks in the horizon could be booked is refused with 409 "already taken on every upcoming week" and nothing is saved.
  - **Each occurrence is saved under the booking lock** (`AvailabilityService.TryAddOccurrence`: the same `pg_advisory_xact_lock` per business+date and overlap re-check as `TrySaveOrDetectConflict`, but a conflict only drops that one occurrence, logged as a `slot_unavailable` skip). The cron (`GenerateOccurrences`) handles one series at a time, saved on its own, and logs and skips a series that fails. Before 2026-10-21 (QA run 4) generation skipped the lock and saved every series at once: two series on one slot made the daily cron fail for every business, and parallel creates double-booked.
  - **Deleting a series** (`DELETE /api/admin/recurring/{id}`) cancels every not-yet-completed appointment it generated (an occurrence the customer moved meanwhile is reloaded and cancelled where it is now, up to 3 tries) (`Status = CANCELLED`, freeing the slot) before removing the series row; already-completed history is left untouched (its stored `Status` is always `CONFIRMED` — see [Appointment status](#appointment-status-no-manual-complete) — so the cancel loop checks `AppointmentStatusHelper.EffectiveStatus` per row, not the raw column).
  - **No pause/resume** — deliberately removed; deleting is the only lifecycle action exposed to the owner besides creating. `IsActive` still exists on the model purely for the auto-deactivation cases above.
  - **Creating a series** (`pages/admin/RecurringAppointmentsPage.tsx`): the owner picks an item, a customer (`CustomerPicker`), then a **day-of-week button** (Sun–Sat, not a raw date picker), then a **time slot from the real availability grid** for the nearest upcoming date on that weekday (same `GET /api/admin/appointments/availability` endpoint the one-off modal uses) — never a free-typed time. That computed date becomes the series' `StartDate`.
  - **`RecurringSeries.Force`** — the recurring equivalent of the one-off `force` flag above, surfaced
    via the same "Enter custom time" toggle + checkbox UI. Unlike the one-off flag (a per-request
    parameter), this has to be a *persisted* column: `GenerateForSeries` re-checks availability for
    every future occurrence, forever, not just at creation, so a day-of-week with no active
    `WorkingHours` row would otherwise silently produce a `RecurringSkip("slot_unavailable")` every
    single week even if the initial creation were somehow force-allowed. When `Force` is set, the
    generation loop skips `GetAvailableSlots` entirely and only still blocks on an exact conflicting
    appointment via `HasConflictingAppointment` — same semantics as the one-off path, just evaluated
    on every occurrence instead of once.
  - `GET /api/cron/generate-recurring` (`CronController`) is the production trigger — same `CronSecret` bearer-auth pattern as `/api/cron/reminders` — triggered once daily by `.github/workflows/cron-generate-recurring.yml`.

### Availability Engine
`Services/AvailabilityService.cs` — generates 30-min slots between working hours start/end (per **date**, via `GetEffectiveDay`: a date inside a pending/running `PresetSchedule` range uses that preset's days+breaks, a date after a running range uses the Default preset it reverts to, otherwise the live `WorkingHours`/`Breaks` template — live hours alone are only right on the near side of a range boundary, since the cron only swaps them on `StartDate`/after `EndDate`; `frontend/src/lib/effectiveSchedule.ts`'s `presetForDate` mirrors this for the Schedule page and dashboard calendar), then removes any slot that overlaps with: breaks, blocked slots, or existing CONFIRMED appointments. Also drops slots where `startTime + itemDuration > workingHours.EndTime`. For **today's date specifically**, also drops any slot whose start time is at or before the current time — a customer booking at 15:00 can't grab a 10:00 slot. `WorkingHours`/`Appointment` start/end times (`"09:00"`, `"17:30"`, ...) are the business's local wall-clock hours and are never converted to/from UTC anywhere in this app, so "now" is `Services/AppClock.Now`: the current time in Israel (`Asia/Jerusalem`), whatever the server's own zone. Never `DateTime.UtcNow`, and never `DateTime.Now`: Railway's containers run in UTC, so `DateTime.Now` there was 2-3 hours behind (until 2026-10-20 today's past slots stayed bookable that long, appointments turned COMPLETED late, and the "3 hours before" reminder went out after the appointment had started). Tests build their dates with `AppClock.Now` too.

**Customer-facing block reason**: `AvailabilityService.GetFullDayBlockInfo(businessId, dateStr)` — called by `BookingController`'s two public availability endpoints only when `slots` comes back empty — looks up a full-day `BlockedSlot` for that date and returns its optional `Reason`. The response shape is `{ slots, isBlocked, blockedReason }`; `BookingWizard.tsx`'s step 3 shows "The business is closed on this date" (`closedByOwner`) plus the reason text when `isBlocked` is true, instead of the generic `noTimes` message — so a customer sees *why* a date has no times, not just that it doesn't.

### Appointment status: no manual "Complete"
The business can only cancel an appointment now (`AdminController.UpdateAppointmentStatus` rejects any `status` other than `CANCELLED`) — there's no "Mark Complete" button anywhere in the admin UI. Instead, `Services/AppointmentStatusHelper.EffectiveStatus(status, date, endTime)` computes "COMPLETED" automatically for any still-`CONFIRMED` appointment whose end time has passed (compared against `AppClock.Now`, Israeli time — same reasoning as the Availability Engine above), applied wherever a status is returned to a client: `AdminController` (dashboard + appointments list), `CustomerAppointmentsController.GetMyAppointments`, and `BookingController.GetAppointment` (the magic-link view). `CANCELLED` is never overridden. The stored `AppointmentStatus` column itself stays `CONFIRMED` — only the API response's status string is computed; nothing rewrites the DB row.
