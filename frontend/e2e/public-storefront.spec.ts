import { test, expect, request } from '@playwright/test'

const API = 'http://localhost:5280/api'

// Seeds a verified business with one bookable item and a city, and returns its slug/name.
async function seedBusiness(api: Awaited<ReturnType<typeof request.newContext>>, city: string) {
  const slug = `e2e-store-${Date.now()}-${Math.floor(Math.random() * 1000)}`
  const email = `${slug}@example.com`
  const register = await api.post(`${API}/auth/register`, {
    data: { name: 'E2E Storefront Co', email, password: 'password123', slug },
  })
  const { devCode } = await register.json()
  const verify = await api.post(`${API}/auth/verify-email`, { data: { email, code: devCode } })
  const { token } = await verify.json()
  await api.post(`${API}/admin/items`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { nameEn: 'Haircut', nameAr: 'Haircut', nameHe: 'Haircut', durationMinutes: 30, price: 40 },
  })
  await api.patch(`${API}/admin/settings`, {
    headers: { Authorization: `Bearer ${token}` },
    data: { description: 'E2E public storefront', city, isListed: true },
  })
  return { slug, name: 'E2E Storefront Co' }
}

test('a logged-out visitor can view a storefront but not book', async ({ page }) => {
  const api = await request.newContext()
  const { slug, name } = await seedBusiness(api, 'Nazareth')
  await page.addInitScript(() => localStorage.setItem('customerLang', 'EN'))

  // Storefront is public — no WhatsApp wall.
  await page.goto(`/${slug}`)
  await expect(page.getByRole('heading', { name })).toBeVisible()
  await expect(page.getByText('📍 Nazareth')).toBeVisible()
  await expect(page.getByText(/This link has expired or is invalid/)).not.toBeVisible()

  // The Follow button is present but disabled for an anonymous visitor.
  await expect(page.getByRole('button', { name: 'Follow' })).toBeDisabled()

  // Booking still requires a session.
  await page.goto(`/${slug}/book`)
  await expect(page.getByText(/This link has expired or is invalid/)).toBeVisible()
})

test('the browse directory lists a business and filters by its city', async ({ page }) => {
  const api = await request.newContext()
  const cityTag = `E2ECity${Date.now().toString().slice(-6)}`
  const { slug, name } = await seedBusiness(api, cityTag)
  await page.addInitScript(() => localStorage.setItem('customerLang', 'EN'))

  await page.goto('/browse')
  // Default ranked list renders without typing anything.
  await expect(page.getByRole('heading', { name: 'Discover Businesses' })).toBeVisible()

  // Filter to the seeded business's (unique) city and confirm its card shows.
  await page.getByRole('combobox').first().selectOption(cityTag)
  await expect(page.locator('a', { hasText: name }).first()).toBeVisible()
})
