import { test, expect, request } from '@playwright/test'

const API = 'http://localhost:5280/api'

test('visiting a barber link while logged out redirects to login, then returns there and books', async ({ page }) => {
  const slug = `e2e-guest-${Date.now()}`
  const email = `${slug}@example.com`
  const phone = `+1555${Date.now().toString().slice(-7)}`
  const api = await request.newContext()

  await api.post(`${API}/auth/register`, {
    data: { name: 'E2E Guest Barber', email, password: 'password123', slug },
  })
  const login = await api.post(`${API}/auth/login`, { data: { email, password: 'password123' } })
  const { token } = await login.json()
  await api.post(`${API}/admin/services`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { nameEn: 'Haircut', nameAr: 'Haircut', nameHe: 'Haircut', durationMinutes: 30, price: 40 },
  })

  // This test exercises the login-gate + booking flow, not localization — pin the customer's
  // language to English (default is now Hebrew, chosen independently of any barber's own language).
  await page.addInitScript(() => localStorage.setItem('customerLang', 'EN'))

  // A barber's page is now gated behind login (CustomerProtectedRoute) — visiting it directly
  // while logged out must redirect to /login with the original path preserved as ?next=.
  await page.goto(`/${slug}`)
  await expect(page).toHaveURL(new RegExp(`/login\\?next=%2F${slug}`))

  await page.getByPlaceholder('+1234567890').fill(phone)
  await page.getByRole('button', { name: 'Send Code' }).click()
  await expect(page.getByText(/Dev mode/)).toBeVisible()
  await page.getByLabel('First Name').fill('Guest')
  await page.getByLabel('Family Name').fill('Person')
  await page.getByRole('button', { name: 'Verify' }).click()

  // Login must send them back to the exact barber page they originally tried to open.
  await expect(page).toHaveURL(new RegExp(`/${slug}$`));
  await expect(page.getByRole('heading', { name: 'E2E Guest Barber' })).toBeVisible()

  await page.getByRole('button', { name: /Book an Appointment/ }).click()
  await page.getByText('Haircut', { exact: false }).first().click()

  await expect(page.getByText('Select a Date')).toBeVisible()
  // Pick the *second* enabled date cell, not the first (which may be today) — today's
  // availability now correctly excludes already-passed times, so if this test runs late in
  // the business day, today alone could have zero slots left. A different day always has its
  // full range available regardless of what time this test runs.
  await page.locator('.grid.grid-cols-4 button').nth(1).click()

  await expect(page.getByText('Select a Time')).toBeVisible()
  await page.locator('.grid.grid-cols-3 button').first().click()

  await expect(page.getByText('Your Details')).toBeVisible()
  // Authenticated now, so name/phone are prefilled from the account created above (and the
  // phone field is locked) — just confirm.
  await expect(page.locator('#booking-name')).toHaveValue('Guest Person')
  await expect(page.locator('#booking-phone')).toHaveValue(phone)
  await expect(page.locator('#booking-phone')).toBeDisabled()
  await page.getByRole('button', { name: 'Confirm Appointment' }).click()

  await expect(page.getByText('Appointment Confirmed!')).toBeVisible({ timeout: 10000 })
})
