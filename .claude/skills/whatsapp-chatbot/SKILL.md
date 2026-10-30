---
name: whatsapp-chatbot
description: barber-saas WhatsApp: the Baileys bridge, chatbot flows (rule-based and OpenAI), lockout, cancel confirmation, language detection, Inquiry mode, the owner Chats page, and customer login via WhatsApp link or phone OTP. Use when touching WhatsAppController, ChatsController, whatsapp-bridge/ or customer auth.
---

<!-- Moved out of CLAUDE.md (2026-10-25) so it loads only when needed. -->

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
- `DELETE /api/platform-admin/businesses/{id}/whatsapp/link` — unlinks and clears `Business.WhatsAppNumber`. If the bridge is down it still clears the number (that alone stops the bot) and returns `bridgeUnreachable: true`.

**Logout alert (2026-10-21)**: when WhatsApp itself ends a linked session (`DisconnectReason.loggedOut`: device removed on the phone, or dropped by WhatsApp) the bridge deletes the saved session and calls `POST /api/whatsapp/bridge/session-event` `{ businessId, event: "loggedOut", phoneNumber }` (`X-Bridge-Secret`, up to 5 tries a minute apart). The backend writes a `WhatsAppLink.LoggedOut` activity row and emails the platform admin through `Services/PlatformAdminAlerts` (system `IEmailSender` chain / Brevo, the same recipients as the Gmail health alert), with a link to the business page to re-link. It also clears `Business.WhatsAppNumber`, so the admin page shows "Link WhatsApp" again (the feed labels the row "WhatsApp bot logged out (system)"). A repeat within 10 minutes while the number is still cleared is ignored (one at a time per business); a logout after a re-link is reported again. An admin's own Unlink sets `session.unlinking`, so it isn't reported.

`WhatsAppBridgeClient`'s linking calls (start/status/unlink) turn any connection/HTTP failure into
`WhatsAppBridgeUnavailableException` (a subclass of `HttpRequestException`), which
`GlobalExceptionHandler` answers with a 503 and a readable message. `POST /api/whatsapp/bridge/inbound`
checks `X-Bridge-Secret` in `Filters/RequireBridgeSecretAttribute` (an authorization filter, so it runs
before body validation) and strips NUL characters from the message and profile name; any Postgres
"NUL in text" error (SQLSTATE 22021) elsewhere is a 400.

**Twilio's WhatsApp Business API integration is kept in the codebase, unregistered, as a dormant
fallback** (`TwilioWhatsAppSender`, and `WhatsAppController`'s original `POST /api/whatsapp/webhook`
with its Twilio-signature validation) — in case a real registered business + Trust Hub approval ever
happens later. Nothing in production currently points at it (no business has a real Twilio WhatsApp
number). `Twilio:AccountSid`/`Twilio:AuthToken` config still exists for this dormant path and for
`TwilioOtpSender` (phone+OTP customer login, a separate Twilio product — Programmable SMS, unrelated
to WhatsApp).
- `WhatsAppController.ProcessMessageAsync` is the shared dispatcher for both the bridge-inbound and
  legacy Twilio paths. It handles one message at a time per (business, phone) using a striped in-memory `SemaphoreSlim` (a single backend instance). Two messages arriving together used to both insert the conversation row and 500 on its unique index — see the AI layer subsection below for what it dispatches to.
- `WhatsAppConversationState` rows are dead once `ExpiresAt` passes (every read filters or resets
  them); `Services/ConversationStateCleanupWorker` deletes rows expired more than a day ago, hourly
  (`ConversationCleanup:WorkerEnabled`, off in tests).
- Reminders (day-before and ~3-hours-before, see `/api/cron/reminders` above) are sent via an
  external cron-job.org job calling the endpoint every 15 minutes — see the cron scheduling note
  earlier in this doc for why this isn't a GitHub Actions workflow.
- **Lockout after repeated invalid replies** (rule-based path only): 3 consecutive replies that
  don't answer the bot's last question -- the "which service?" list, the Book/Ask gate (Inquiry
  mode), or the cancel question (2026-10-25, owner's request: the gate used to re-show forever and
  the cancel question dropped after one re-ask) -- (`WhatsAppConversationState.InvalidAttempts`,
  `WhatsAppController.MaxInvalidAttempts`; reset to 0 whenever the bot sends a new message: a new
  list, gate, question or booking link; a cancel/reschedule keyword at the cancel question starts
  over rather than counting) stops the bot dispatching to anything else -- including
  cancel/reschedule keywords -- until the customer sends the literal unlock keyword (`"#"` since 2026-10-23, the old `"$"` still works;
  `WhatsAppController.UnlockKeyword`), which restarts the conversation from the opening prompt (the Book/Ask gate when Inquiry mode is on).
  While locked out (other than the unlock keyword itself) the `whatsapp.tooManyInvalidReplies` reminder is
  re-sent at most every 30 minutes and other messages get no reply (2026-10-20, owner's choice: a
  reminder on every message was spam; 2026-09-22's fix had replaced total silence, which looked
  broken). The lock lasts 24 h from the last reminder (`LockoutLifetime`); `ExpiresAt - LockoutLifetime`
  is when that reminder went out, so no extra column (`StartLockout`/`LockoutReminderIfDue`).
- **No-show question in the day-before reminder** (2026-10-25, `Services/AttendanceQuestion`): the
  reminder ends with `reminder.confirmQuestion` ("Reply 1 to confirm or 2 to cancel") when the chatbot
  is on, the business isn't disabled and the customer has no live conversation row (a service list,
  cancel question or owner chat would read the same 1/2; they get the plain reminder), at most once
  per phone per business per run. The cron saves after each reminder; if the customer's own message
  created their conversation row mid-run, the question is dropped and the reminder stays sent. It's parked as
  `ChatbotMode = "ConfirmAttendance:{appointmentId}"` until the appointment starts, and
  `Appointment.AttendanceStatus = "ASKED"`. `WhatsAppController.AnswerAttendanceQuestionAsync` runs first
  in `ProcessMessageLockedAsync` (before the Book/Ask menu): `1`/yes or a keep word ("keep it",
  "להשאיר") -> `"CONFIRMED"` + `whatsapp.attendanceConfirmed`; `2`/no -> `CancelConfirmedAppointment`
  (owner approval and waitlist included); any other message with a cancel word ("yes cancel", "don't
  cancel", "לא לבטל") gets `whatsapp.attendanceReask` once (`InvalidAttempts = 1` marks it), after which
  a non-negated cancel word cancels; a reschedule word or anything else drops the question and is
  handled normally. Moving
  an appointment clears `AttendanceStatus` (with `ReminderSent`). The owner sees it as a badge
  (`components/admin/AttendanceBadge`) on Appointments and in the calendar's details.
- **Cancel keyword asks first** (rule-based path 2026-10-11; the AI path asks too since 2026-10-19, see below): cancel/reschedule
  keywords match **whole words only** (`ContainsKeyword`) — the old substring match cancelled real
  appointments on ordinary sentences like "אני בטלפון עכשיו" ("בטל" inside "בטלפון"); "שנה" was
  dropped from the reschedule list since it usually means "year". Words are compared without
  combining marks (harakat/niqqud), and an Arabic word also matches without its `و`/`ال` prefix
  (`ContainsKeyword`/`Words`). A cancel keyword then names the nearest cancellable appointment
  (date, then start time) and parks it in `WhatsAppConversationState.ChatbotMode` as
  `"ConfirmCancel:{id}:{askedUnixSeconds}:{reasked}"` (no extra column/migration); with several
  upcoming ones it first asks which (`whatsapp.pickCancel`, numbered, up to 5,
  `"PickCancel:{id,id,...}:..."`; `0` keeps them all) and the number leads to the 1/2 question for
  that one. `AnswerCancelPrompt` (shared by both paths): `1` (or a yes word, alone or with filler
  words like "please" -- "yes see you" isn't one) cancels via `CancelFromCustomerAsync`, `2` keeps it;
  a short unclear reply (up to 4 words, no `?`, not a reschedule) is asked once more, anything else
  drops the question and is handled as a normal message. The question lives 10 minutes by its own
  clock even when a pending booking link keeps the row for 24 h (QA run 4). A reschedule keyword
  with several upcoming appointments sends every appointment's change-time link
  (`whatsapp.rescheduleLinks`). When owner approval is required the reply is
  `whatsapp.cancellationRequested`, not `whatsapp.cancelled` (also for the AI path's cancel tool),
  and a later "cancel" with only pending ones left gets `whatsapp.cancellationAlreadyRequested`.
- **Closed businesses**: a platform-disabled business (`IsDisabled`) gets no replies at all; one that
  can't take bookings (`Business.AcceptsBookings`: expired subscription or ended trial, the same
  check `BookingController` answers 403 with) gets `whatsapp.bookingUnavailable` instead of a service
  list or link (both paths).
- **Numbers and `$` commands** are read leniently: `ParseNumber` accepts `2.`, `#2`, `2)`, fullwidth and
  keycap-emoji digits; `NormalizeDollarCommand` ignores direction marks (our HE/AR texts wrap `$2` in
  them, so a copy-paste carries them), spaces and fullwidth `＄`, also for the `$` unlock.
- **Quiet while a booking link is pending** (rule-based path only): once a booking link is issued
  (`WhatsAppController.IssueBookingLink`), the conversation-state row is kept alive (not removed)
  with `AwaitingBookingCompletion = true` instead, valid for the same 24h the link itself is --
  any further message from that phone gets no automated reply at all, rather than re-sending the
  opening prompt, except cancel/reschedule keywords and the answer to the cancel confirmation they start (2026-10-13; QA found "cancel" ignored for 24 h), while the customer finishes booking on the web page the link opened.
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
to call a tool (`create_booking_link`, `cancel_upcoming_appointment`, plus `mark_unclear` for the
lockout); the tool implementations build the exact same `I18nService`-templated text the rule-based path already uses
(shared via `IssueBookingLink`/`FindUpcomingAppointments`), and the system prompt instructs
the model to relay a tool's `message` field verbatim rather than reword it. This is what prevents a
hallucinated URL, price, or date reaching a customer — the model only freely composes text for
open-ended Q&A (hours, prices, greetings) grounded in the business data (name, active items,
language) injected into its system prompt each turn. Rescheduling isn't a separate tool — the
system prompt tells the model to call `cancel_upcoming_appointment` then `create_booking_link`.

**Lockout after repeated unclear messages** (2026-09-21, changed 2026-10-19): mirrors the rule-based path's
`"$"`-unlock lockout above, reusing the same `WhatsAppConversationState.InvalidAttempts` field and
`MaxInvalidAttempts`/`UnlockKeyword` constants, but counted differently here — a turn counts as
"invalid" only when the model calls the third tool, `mark_unclear` (a message it can't understand or
that has nothing to do with the business). Questions it answers don't count (QA: three plain
questions used to lock the customer out, when any turn without a booking/cancel tool counted);
`AiUsageLimiter` limits the cost instead. Any other turn resets the counter to 0. On the 3rd unclear message in a row, the AI's own reply text is
replaced with the `whatsapp.tooManyInvalidReplies` message (same as rule-based); after that the AI
isn't called at all and the reminder repeats at most every 30 minutes (same as rule-based, see above), until the customer sends the literal `"#"` (or the old `"$"`), which clears the
conversation state entirely and replies with `whatsapp.aiConversationRestarted` — a fresh state row
only gets created once the customer sends a real next message, same as the rule-based unlock.

**Cancel asks 1/2 first (2026-10-19), like the rule-based path:** `cancel_upcoming_appointment` cancels
nothing; it returns the `whatsapp.confirmCancel` question for the nearest cancellable appointment
(or `cancellationAlreadyRequested`), that text replaces the model's reply, and the id is parked in
`ChatbotMode` as `ConfirmCancel:{id}`. The next message's `1`/`2` is answered at the top of
`ProcessMessageWithAiAsync` without OpenAI (`CancelConfirmedAppointment`, shared with the rule-based
path); anything else drops the question and goes to the model. The row and its history are kept.
`create_booking_link` is refused in the same turn as the question. If OpenAI fails *after* a tool
already acted, the tool's own `message` is sent instead of falling back to the rule-based path (which would
answer as if nothing happened). `Services/AiUsageLimiter` (singleton, in memory) sends at most
`OpenAI:MaxMessagesPerPhonePerDay` (default 50) messages per (business, phone) per rolling 24 h to OpenAI;
past that the rule-based bot answers.

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
- Three distinct touchpoints in the customer's journey, each per-language now (`...En`/`...Ar`/`...He`
  suffixed nullable free text fields, mirroring `Item.NameEn/NameAr/NameHe`, plus a
  `Business.Resolve<X>Message(lang)` method picking among them — see below): `ChatbotWelcomeMessage*`
  replaces the opening greeting (item list and instructions still stay in the detected language, so
  a custom welcome is followed by `whatsapp.selectServicePrompt`, not the full `whatsapp.selectService`
  template); `ChatbotConfirmationMessage*` is sent alongside the booking *link* (`IssueBookingLink`,
  may include a literal `{url}` placeholder, else the link is appended on its own line); `ChatbotFinalMessage*`
  replaces the default text `BookingController` sends once the appointment is *actually created*
  (`whatsapp.bookingConfirmed`) -- the last message in the journey. It takes the same placeholders
  as the default (`{customerName}`, `{businessName}`, `{service}`, `{date}`, `{time}`, via
  `I18nService.Fill`); one with neither `{date}` nor `{time}` gets a `📅 date · time · service` line
  appended, since it used to drop the booking details entirely. A business writes each message per language exactly like item names; when the
  customer's resolved conversation language (`lang`) has no custom text set for that message,
  `Resolve<X>Message` returns `null` and every call site's existing `IsNullOrWhiteSpace` check falls
  through to the built-in `I18nService` default for that language -- it never borrows another
  language's custom text.

**Language auto-detection** (`WhatsAppController.DetectLanguage`): every inbound message's script
is checked against the Hebrew (`U+0590`–`U+05FF`) and Arabic (`U+0600`–`U+06FF`) Unicode blocks,
falling back to `EN` if it has any Latin letters at all. This is independent of the business's own
configured storefront `Language` — the bot always replies in whatever language the *customer* just
typed in. The Arabic block check counts **Arabic-Indic/Extended Arabic-Indic digits** (`٠`-`٩` /
`۰`-`۹`, see below) as a genuine Arabic signal too, not just Arabic letters — confirmed with the
business owner (2026-09-21) that, unlike ASCII digits, a customer only sends these because their
keyboard/locale is actually Arabic, so a bare Arabic-Indic-digit reply switches/keeps the
conversation in Arabic. A message with no script signal at all (a bare ASCII numeric reply like
`"1"`, punctuation, or an emoji) carries no signal of its own, so `ResolveLanguage` falls back to
the language already stored on the open `WhatsAppConversationState` row (see below) for that phone,
and only falls back to the business's own default when there's no open conversation either (a
signal-less first message).

**`ResolveLanguage` is called exactly once per inbound message, in `ProcessMessageAsync`, and
threaded through as a parameter** to `HandleInquiryModeAsync`, `ProcessMessageWithAiAsync`, and
`ProcessMessageRuleBasedAsync` — none of the three re-resolve it independently (fixed 2026-09-21;
they each used to call `ResolveLanguage` again on their own). This matters because
`HandleInquiryModeAsync`'s "1" and `$1` (both mean "back to booking") branches call
`ClearConversationState` and then fall through (`Handled=false`) to the booking dispatch for the
*same* message — if that dispatch re-resolved language afterward, it would find no conversation
state left to read (just cleared) and wrongly fall back to the business's default language for a
signal-less reply like a bare `"1"`, even though the language had already correctly resolved to
whatever the customer was using moments earlier in the very same request. If this method's
signature changes again, keep `lang` as a parameter, not an internal `ResolveLanguage` call.

`WhatsAppConversationState.Language` and `WhatsAppBookingToken.Language` both persist the resolved
language — the latter is returned by `POST /api/customer/auth/whatsapp` (`language` field) and the
frontend's `loginWithWhatsAppToken` calls `setLang()` with it, so the booking wizard opens in the
same language the customer was just chatting in, not whatever was last stored in this browser. The error responses (expired 400, item-gone 404) carry it too (`WhatsAppBookingTokenService.FindLanguageAsync`, since expired rows are never deleted), so the "link expired" page shows in the chat language. A token that was never issued falls back to the browser language when this browser has no saved choice.

That "business's own default" is `Business.ChatbotDefaultLanguage` (nullable, `Settings >
Chatbot Settings`, falling back to the general `Language` field when unset) — kept as a separate
field rather than reusing `Language` outright, since `Language` also drives the admin
dashboard/storefront locale and an owner may want the chatbot to default to a different language
than their own dashboard.

**Arabic-Indic numeral replies** (`WhatsAppController.NormalizeDigits`): a numbered-selection reply
in Arabic-Indic (`٠`-`٩`) or Extended Arabic-Indic/Persian (`۰`-`۹`) digits is translated to ASCII
before `int.TryParse` in `TryHandleServiceSelectionReply` — a customer replying in Arabic script
naturally types the number in one of these, not by switching to a Western keyboard.
`HandleInquiryModeAsync`'s own gate ("1"/"2") and `$1`/`$2` commands run `NormalizeDigits` on the
trimmed message too, for the same reason — comparing the raw text directly against the literal
ASCII commands would silently reject an Arabic-Indic reply at the gate even though the identical
reply works fine once past it, on the real service list.

These same digit ranges sit inside the Arabic Unicode block (`U+0600`–`U+06FF`), so `DetectLanguage`
counts them as an `AR` signal the same as an Arabic letter would (see above) — this was flipped
2026-09-21 from an earlier version that explicitly excluded them (`IsArabicIndicDigit` is still used
for `NormalizeDigits`, just no longer for language detection); if this area needs touching again,
don't reintroduce that exclusion without re-confirming with the owner first.

### Chatbot Inquiry mode (optional, alongside booking)
`Business.ChatbotInquiryEnabled` (default `false`, `Settings > Chatbot Settings`) lets a customer
step out of the booking flow entirely to ask a free-form question or reach the owner directly.
Handled in `WhatsAppController.HandleInquiryModeAsync`, called **before** either chatbot path
(rule-based or AI) ever runs — a business that doesn't enable this sees zero behavior change, since
the check is skipped outright.

**Opening gate, not the service list.** When Inquiry is enabled, a customer's very first message (no
open `WhatsAppConversationState` row, or one left in `AwaitingModeChoiceMode`) gets a plain two-option
gate (`whatsapp.modeGate` / the welcome-message-tail variant `whatsapp.modeGatePrompt`) instead of the
service list:
```
1. Book a service
2. Ask a question / talk to us
```
Replying `1` clears any gate state and falls through to the normal rule-based/AI dispatch, which then
shows the real numbered service list. Replying `2` enters Inquiry mode directly. Anything else re-shows
the gate. Once *past* the gate — mid-booking, looking at the actual service list — the plain `1`/`2`
digits are already spoken for (service selection), so switching modes from there uses the `#`-prefixed
commands instead (`#1`/`#2` since 2026-10-23, the owner's choice; the old `$1`/`$2` still work), checked
ahead of the gate/service logic:
- `#1` — back to Booking mode, also when sent at the opening gate (2026-10-13; it used to be read as a service number there). The conversation-state row is *removed* outright (not just flipped),
  matching the reschedule keyword's own `ClearConversationState` call — merely updating the row
  would leave it looking "open" to `TryHandleServiceSelectionReply`, which would then try (and fail)
  to parse the literal text `"$1"` as a numeric service choice instead of falling through to a
  fresh prompt. The same clear-not-update rule applies to the gate's own `1` reply, for the identical
  reason.
- `#2` — enters Inquiry mode (`WhatsAppConversationState.ChatbotMode = "Inquiry"`), same as the gate's
  `2` reply (both go through the shared `EnterInquiryModeAsync` helper).

`1#`/`2#`, `$1`/`$2` and `1$`/`2$` work too (`NormalizeDollarCommand` turns them all into `#1`/`#2`: RTL keyboards, and how the command used to render in Hebrew/Arabic
text; the AR/HE strings now wrap it in `\u200E` marks). `$1` also clears a live service-list row, so it never
counts as an invalid reply, and a row with a booking link pending (it used to be kept, so `$1` after a link +
`$2` got silence and Inquiry mode stuck for 24 h); entering Inquiry mode clears `AwaitingBookingCompletion`. A locked-out conversation (`InvalidAttempts >= 3`) skips this method entirely, so
only `#` (or `$`) unlocks it. An Inquiry-mode row lives 24 h (`InquiryStateLifetime`), not 10 min, so a later
follow-up is still logged.

While in Inquiry mode, every further message from that phone is logged as a `ChatbotInquiry` row
(and as a `ChatMessage` for the owner's Chats page, see [Owner Chats page](#owner-chats-page)) with **no reply** (2026-10-23,
owner's choice: the owner is talking to the customer, and an acknowledgement for every message was
spam; `whatsapp.inquiryStarted`, sent once on entering, says how to get back), and is **not**
dispatched to booking logic at all, until the customer sends `#1`. `PromptServiceSelection`
(rule-based path) always resets `ChatbotMode` back to `"Booking"` on every fresh prompt it issues —
without this, a row left over from an expired/exited Inquiry session would still read as Inquiry mode
once its `ExpiresAt` gets refreshed by the next prompt, silently swallowing the customer's next numeric
service-selection reply as if it were inquiry content instead.

Once past the gate, the rule-based service-list message gets a `whatsapp.inquiryEscapeHatch` note
appended (`PromptServiceSelection`) reminding the customer that `$2` switches to Inquiry mid-booking;
the AI path doesn't call that function at all, so it instead gets one extra line in its system prompt
(`BuildAiSystemPrompt`) telling the model to mention `$2` when it can't help with something -- the
model never handles the command itself, since `$2` is still intercepted before the AI path is ever
reached.

**Owner notification**: `WhatsAppController.SendInquiryNotificationsAsync` fires **once per inquiry
session**, not per message (`WhatsAppConversationState.InquiryNotified`, reset whenever the customer
returns to Booking mode via `$1`, so a *later*, separate inquiry still notifies again) — best-effort
on both channels, a failure on either never blocks the customer's own reply or the always-on
in-app log. Two independent settings, each needing its own contact value to actually fire:
`InquiryNotifyViaWhatsApp` + `InquiryWhatsAppNumber` (sent from the business's own linked bot number
to a *different* recipient — the owner's own number — via the existing `IWhatsAppSender`, not a
reply in the customer's thread) and `InquiryNotifyViaEmail` + `InquiryEmail` (a blank contact falls back to `Business.Phone`/`Business.Email`, like the cancel-approval channels; via the platform's Gmail,
`IOwnerEmailSender` — the same sender as the owner-email composer; moved off the Brevo
`IEmailSender` chain 2026-09-29 so every email to a business owner comes from the same account).

The old `/api/admin/chatbot-inquiries` endpoints and the `ChatbotInquiries` writes were removed
2026-10-30 (QA run 5): the messages live as `ChatMessage`s; the table is left in the database, unused. Frontend: `pages/admin/ChatbotSettingsPage.tsx`, its
own top-level sidebar page (`/admin/chatbot`, separate from the general `/admin/settings` page) —
holds the enable toggle, all three chatbot messages and the inquiry toggle (its Inquiries inbox and the
notification contacts moved to `/admin/chats` 2026-10-25 at the owner's request -- the alert settings are its ⚙️ Settings,
`components/admin/ChatAlertsModal`, the same partial PATCH), so every
chatbot-related control lives in one place instead of being buried in general Settings. It PATCHes
`/api/admin/settings` with only its own fields, same partial-update endpoint the general Settings
page uses for the rest of the business record.

### Owner Chats page
`/admin/chats` (2026-10-25, owner's request) is where the owner answers customers who chose to talk
to them in the chatbot -- Inquiry mode (`#2`, or `2` at the Book/Ask menu), so it needs
`ChatbotInquiryEnabled` (the page says so when it's off). A chat is **open** while that customer's
conversation-state row is live in Inquiry mode (`Services/ChatLog.IsChatOpenAsync`); only then are
`ChatMessage` rows recorded (`Sender`: `Customer` / `Bot` / `Owner` / `OwnerPhone`):
`WhatsAppController.BridgeInbound` saves the customer's message (unread) when the chat was already
open, and the bot's reply on the way in or out (`inquiryStarted`, `#1`'s reply); booking-flow
messages, reminders and other automated sends are not recorded. The owner (`ChatsController`, all
409 "This chat has ended" for a finished chat, 404 for a phone with no chat -- no cold messages,
400 with no linked number, 502 when the bridge can't send):
- `POST .../chats/messages` `{ phone, text }` -- sent through `IWhatsAppBridgeClient` with a first
  line `whatsapp.fromOwner` ("💬 Message from the business owner:") in the conversation's language;
  stored without that line.
- `POST .../chats/attachment` (multipart `phone`, `caption?`, `file`) -- a photo (JPG/PNG/WEBP, sent as a
  WhatsApp image) or a PDF/Word/Excel document, up to 10MB (`Services/ChatFiles`: extension allowlist +
  the file's first bytes must match). Kept in the database as a `ChatAttachment` (the backend's disk
  doesn't survive a deploy), linked from the `ChatMessage` (`AttachmentId`); the caption gets the same
  owner first line. Sent to the bridge inline as base64 (`IWhatsAppBridgeClient.SendMediaAsync`, the
  bridge's `/send` takes `media` and a 15mb JSON body). `GET .../chats/attachments/{id}` serves the file
  to the owner only (the page fetches it with the token as a blob, `nosniff`).
- **The customer's photos/files** (2026-10-25): the bridge downloads a customer message's image, video,
  voice note, sticker or document (up to 16MB, `MAX_MEDIA_BYTES`) and sends it on `bridge/inbound` as
  `media { type, mimetype, fileName, base64 }` (`Message` = its caption). Kept as a `ChatAttachment` only
  while the chat is open; the type is narrowed to a known list (`ChatFiles.InboundContentType`, else
  `application/octet-stream`, so HTML/SVG never renders), an unnamed photo/voice note gets `photo.jpg`/
  `voice.ogg`. Without bytes (too big, download failed) the message reads `📎 name`. A caption-less file
  outside a chat gets no reply. The page shows photos, plays audio/video, and downloads the rest.
  Since QA run 5 the bridge ignores `@broadcast`/`@newsletter` (status, channels), asks
  `POST bridge/media-wanted { businessId, fromPhone }` before downloading (open chat + quota; false on
  any error, so an older backend just means names only), and streams the download, stopping at 16MB.
  Quota (`ChatFiles.InboundQuotaAllowsAsync`, rolling 24h): 20 files / 50MB per customer, 500MB per
  business; past it the file is kept by name only. Retention (owner's choice 2026-10-30): chat files
  (customer and owner) are deleted after 90 days by `ConversationStateCleanupWorker.DeleteOldAttachmentsAsync`
  (hourly); the message stays, reading `📎 name`. Chat text is kept.
- `POST .../chats/booking-link` `{ phone, itemId }` -- a normal `WhatsAppBookingToken` link
  (`whatsapp.ownerBookingLink`, same first line) for a bookable service the owner picks; the chat stays open.
- `POST .../chats/finish` `{ phone, sendServiceList }` -- sends `whatsapp.ownerChatEnded`, plus the
  bot's numbered list (`Services/ServiceListPrompt.PromptAsync`, shared with the chatbot, leaving a
  Booking-mode row waiting for the number) when `sendServiceList`; otherwise the row is removed. The
  chatbot answers that customer again either way.
**⚡ Ready messages** (`components/admin/ReadyMessagesModal`) put a text into the message box (the owner
still edits and sends it): "From the system" -- `GET .../chats/system-message?phone=&kind=&date=&itemId=`,
built by `Services/ChatTemplates` from live data in the conversation's language (`template.*` in
`I18nService`): `services` (all with duration/price), `service`, `week` (next 7 days via
`AvailabilityService.GetUpcomingHours`, so presets and full-day blocks count), `day`, `freeTimes` (the
booking page's slots for a service on a date), `address` (+ map link), `page` (storefront link),
`appointments` (this phone's upcoming ones); and "My messages" -- the owner's own `QuickReply` rows
(`GET/POST .../chats/quick-replies`, `PUT/DELETE .../{id}`, up to 100, as typed).
Clicking the customer's name in a chat offers **Book an appointment** (`NewAppointmentModal` with
`initialCustomer`) or **Add to recurring appointments** (navigates to `/admin/recurring` with the
customer in router state; the form opens on it once) or **Their appointments** (`/admin/appointments` with the
search set to their phone and the date filter on All, same router-state pattern). The customer is their `Customer` row when the
chat's `customerId` is set, else a new customer from the WhatsApp name (first word / rest) and phone,
editable in `CustomerPicker`, which now starts on a value it's given.
Replying (dashboard or phone) marks the chat read and keeps the Inquiry row alive for another
`InquiryStateLifetime`. The bridge reports messages typed on the linked phone (`fromMe`, minus the
ids it sent: `sendTracked` picks the id before sending, since Baileys echoes on `process.nextTick`)
to `POST /api/whatsapp/bridge/outgoing`, recorded as `OwnerPhone` only in an open chat. The page polls
(list 5 s, open chat 3 s, sidebar count 10 s); `POST .../chats/read` is `[SkipActivityLog]`.

### Customer login via WhatsApp
The primary way a customer session starts: redeeming a link the WhatsApp bot sent them (no
sign-up/sign-in step at all). Phone+OTP (below) is a second, parallel path — both just mint the
same kind of `CustomerAccount`-backed JWT. Flow (`WhatsAppController` + `CustomerAuthController`):
1. Any message from a phone with no pending selection (or the `book` keyword) gets a numbered list
   of the business's active items (`whatsapp.selectService`, alphabetical in the reply language since 2026-10-13; it used to be `Item.Id` order). The shown ids are saved in `WhatsAppConversationState.ShownItemIds` (migration `AddConversationShownItemIds`), so a reply number means what the customer saw even if the owner changed the services since. Only a `Booking`-mode row **with** `ShownItemIds` counts as waiting for a number (`TryHandleServiceSelectionReply`); an AI-path row (the AI path clears `ShownItemIds` every turn), the gate or a leftover Inquiry row gets a fresh list instead — they used to be read as one, so a bare number booked an unseen service and free text counted toward the lockout (QA run 3). A service turned off in the meantime gets `whatsapp.serviceNoLongerAvailable` plus the current list
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
   `{ businessSlug, itemId }`. The frontend then redirects into `/:slug/book?itemId=`, which
   skips straight to date selection (`BookingWizard`'s deep-link `useEffect`) — no sign-up/sign-in
   step, no item list to pick from again.

**Phones are stored in E.164, always.** `PhoneNormalizer.Normalize` (used everywhere phones are
stored/matched) turns a number with no `+` into a local Israeli one (`050-123-4567` →
`+972501234567`) and reads a `00` prefix as `+`, so a number the owner typed locally and WhatsApp's
E.164 `From` are the same customer. It runs at every entry point (`WhatsAppController.ProcessMessageAsync`
for both inbound paths, `AdminController.ResolveCustomer`, recurring create, OTP request/verify,
booking session claim); `PhoneNormalizer.IsValid` (7–15 digits, no letters) rejects garbage on the
OTP request and owner booking/recurring forms. Rows stored before this (2026-10-11) were rewritten by
the data-only `NormalizeStoredPhones` migration, which skips (never merges) a row whose canonical form
would collide on a unique phone index — keep its SQL `tmp_canon_phone` in sync with `Normalize`.

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
in Development, matching `AuthController`'s `devCode` convention). A fixed reviewer phone/code
pair (`CustomerAuth:ReviewerPhone` / `CustomerAuth:ReviewerCode`) always succeeds without sending a
real SMS, for app-store reviewer logins once the mobile app exists. **Off unless both are set**
(2026-10-18; it used to be a hard-coded `0501234567` / `123456`, live in production). The code is
claimed with one conditional `ExecuteUpdateAsync`, so the same code sent twice at once signs in once. `POST /api/customer/auth/verify` `{ phone, otp, name?,
familyName? }` checks the code (attempts/expiry enforced), upserts `CustomerAccount` (name/
familyName required only for a brand-new account), backfills any pre-existing guest-booked
`Customer.CustomerAccountId` row for that phone (same `ExecuteUpdateAsync` pattern the WhatsApp
login uses), and returns the same JWT shape as WhatsApp login.

Frontend: `pages/public/CustomerLoginPage.tsx` (`/login`, phone → OTP two-step form, `?next=`
target, dev-code banner) and `lib/customerAuth.tsx`'s `requestOtp`/`verifyOtp` (alongside
`loginWithWhatsAppToken` — all three end at the same shared `storeSession` helper). Linked from
`CustomerProtectedRoute`'s unauthenticated fallback and `CustomerAccountNav`'s logged-out state.
