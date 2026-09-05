import { test, expect, request } from '@playwright/test'
import crypto from 'node:crypto'

const API = 'http://localhost:5280/api'
// Matches backend appsettings.json's AppUrl for local dev -- WhatsAppController signs against
// {WebhookPublicUrl ?? AppUrl}/api/whatsapp/webhook, and WebhookPublicUrl isn't set locally.
const APP_URL = 'http://localhost:5173'

function computeTwilioSignature(url: string, authToken: string, params: Record<string, string>) {
  const data = url + Object.keys(params).sort().map((k) => k + params[k]).join('')
  return crypto.createHmac('sha1', authToken).update(data).digest('base64')
}

test('opening a WhatsApp booking link logs the customer in with no sign-up step and books', async ({ page }) => {
  const slug = `e2e-wa-${Date.now()}`
  const email = `${slug}@example.com`
  const phone = `+1555${Date.now().toString().slice(-7)}`
  const api = await request.newContext()

  const register = await api.post(`${API}/auth/register`, {
    data: { name: 'E2E WA Barber', email, password: 'password123', slug },
  })
  const { devCode } = await register.json()
  // Registration leaves the barber unverified; verify via API to get a token directly.
  const verify = await api.post(`${API}/auth/verify-email`, { data: { email, code: devCode } })
  const { token } = await verify.json()
  await api.post(`${API}/admin/services`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { nameEn: 'Haircut', nameAr: 'Haircut', nameHe: 'Haircut', durationMinutes: 30, price: 40 },
  })
  // The webhook signature is now checked against one platform-owned Twilio:AuthToken (local dev's
  // dotnet user-secrets), not a per-barber token -- see CLAUDE.md's Twilio/WhatsApp section for
  // the local-dev setup this env var must match. TwilioNumber alone is still per-barber, but it's
  // now assigned by a platform admin rather than settable via /api/admin/settings, so this test
  // bootstraps/logs into a platform-admin account to assign it, below.
  const twilioToken = process.env.TWILIO_AUTH_TOKEN ?? 'test-auth-token'
  const twilioNumber = `+1555${(Date.now() + 1).toString().slice(-7)}`
  const barberId = (await (await api.get(`${API}/admin/settings`, {
    headers: { Authorization: `Bearer ${token}` },
  })).json()).id
  // Fixed (not per-run) credentials -- platform-admin bootstrap only ever succeeds once per DB,
  // so repeat runs against the same local dev DB need to log into the *same* admin account
  // rather than registering a new one each time, matching this suite's "safe to run repeatedly
  // against the same DB" convention.
  const adminEmail = 'e2e-platform-admin@example.com'
  const bootstrapAvailable = (await (await api.get(`${API}/platform-admin/bootstrap-available`)).json()).available
  const admin = bootstrapAvailable
    ? await api.post(`${API}/platform-admin/bootstrap`, { data: { email: adminEmail, password: 'password123', name: 'E2E Admin' } })
    : await api.post(`${API}/platform-admin/login`, { data: { email: adminEmail, password: 'password123' } })
  const { token: adminToken } = await admin.json()
  await api.patch(`${API}/platform-admin/barbers/${barberId}/twilio-number`, {
    headers: { Authorization: `Bearer ${adminToken}` },
    data: { twilioNumber },
  })

  // This test exercises the WhatsApp login-and-book flow, not localization — pin the customer's
  // language to English (default is now Hebrew, chosen independently of any barber's own language).
  await page.addInitScript(() => localStorage.setItem('customerLang', 'EN'))

  // Simulates the two inbound WhatsApp messages a real customer would send: the bot's first reply
  // asks which service, then a numeric reply gets a personal booking link -- there's no way to
  // reach that link other than through this real, signature-validated webhook, matching production.
  const webhookUrl = `${APP_URL}/api/whatsapp/webhook`
  const firstMsg = { To: `whatsapp:${twilioNumber}`, From: `whatsapp:${phone}`, Body: 'hi' }
  await api.post(`${API}/whatsapp/webhook`, {
    headers: { 'X-Twilio-Signature': computeTwilioSignature(webhookUrl, twilioToken, firstMsg) },
    form: firstMsg,
  })

  const secondMsg = { To: `whatsapp:${twilioNumber}`, From: `whatsapp:${phone}`, Body: '1', ProfileName: 'Guest Person' }
  const selectionResp = await api.post(`${API}/whatsapp/webhook`, {
    headers: { 'X-Twilio-Signature': computeTwilioSignature(webhookUrl, twilioToken, secondMsg) },
    form: secondMsg,
  })
  const twiml = await selectionResp.text()
  const linkMatch = twiml.match(/http\S*\/w\/[a-f0-9]+/)
  expect(linkMatch, `expected a booking link in the bot reply, got: ${twiml}`).toBeTruthy()
  const relativePath = linkMatch![0].replace(APP_URL, '')

  // Opening the link logs the customer in (no sign-up/sign-in page) and skips straight to date
  // selection (no service-selection page — already chosen via WhatsApp).
  await page.goto(relativePath)
  await expect(page).toHaveURL(new RegExp(`/${slug}/book\\?serviceId=`))
  await expect(page.getByText('Select a Date')).toBeVisible()
  await expect(page.getByText('Select a Service')).not.toBeVisible()

  // "View My Appointments" now lives on this step (moved from the barber page).
  await expect(page.getByText('View My Appointments')).toBeVisible()

  // Pick the *second* enabled date cell, not the first (which may be today) — today's
  // availability now correctly excludes already-passed times, so if this test runs late in
  // the business day, today alone could have zero slots left. A different day always has its
  // full range available regardless of what time this test runs.
  await page.locator('.grid.grid-cols-4 button').nth(1).click()

  await expect(page.getByText('Select a Time')).toBeVisible()
  await page.locator('.grid.grid-cols-3 button').first().click()

  await expect(page.getByText('Your Details')).toBeVisible()
  // Authenticated now, so name/phone are prefilled from the WhatsApp identity (and the phone
  // field is locked) — just confirm.
  await expect(page.locator('#booking-name')).toHaveValue('Guest')
  await expect(page.locator('#booking-family-name')).toHaveValue('Person')
  await expect(page.locator('#booking-phone')).toHaveValue(phone)
  await expect(page.locator('#booking-phone')).toBeDisabled()
  await page.getByRole('button', { name: 'Confirm Appointment' }).click()

  // Booking redirects straight back to the barber's own page.
  await expect(page).toHaveURL(new RegExp(`/${slug}$`), { timeout: 10000 })
  await expect(page.getByRole('heading', { name: 'E2E WA Barber' })).toBeVisible()
})
