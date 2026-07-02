import { test, expect, request } from '@playwright/test'

const API = 'http://localhost:5280/api'

test('guest can book an appointment end-to-end without an account', async ({ page }) => {
  const slug = `e2e-guest-${Date.now()}`
  const email = `${slug}@example.com`
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

  await page.goto(`/${slug}`)
  await expect(page.getByRole('heading', { name: 'E2E Guest Barber' })).toBeVisible()

  await page.getByRole('button', { name: /Book an Appointment/ }).click()
  await page.getByText('Haircut', { exact: false }).first().click()

  await expect(page.getByText('Select a Date')).toBeVisible()
  // Working hours are Mon-Fri; pick the first enabled date cell.
  await page.locator('.grid.grid-cols-4 button').first().click()

  await expect(page.getByText('Select a Time')).toBeVisible()
  await page.locator('.grid.grid-cols-3 button').first().click()

  await expect(page.getByText('Your Details')).toBeVisible()
  await page.locator('#booking-name').fill('Guest Person')
  await page.locator('#booking-phone').fill('+15550001234')
  await page.getByRole('button', { name: 'Confirm Appointment' }).click()

  await expect(page.getByText('Appointment Confirmed!')).toBeVisible({ timeout: 10000 })
})
