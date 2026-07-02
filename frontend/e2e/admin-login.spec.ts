import { test, expect, request } from '@playwright/test'

const API = 'http://localhost:5280/api'

test('barber can log in and land on the dashboard', async ({ page }) => {
  const slug = `e2e-admin-${Date.now()}`
  const email = `${slug}@example.com`
  const api = await request.newContext()

  await api.post(`${API}/auth/register`, {
    data: { name: 'E2E Admin Barber', email, password: 'password123', slug },
  })

  await page.goto('/admin/login')
  await page.getByPlaceholder('Email').fill(email)
  await page.getByPlaceholder('Password').fill('password123')
  await page.getByRole('button', { name: 'Log In' }).click()

  await expect(page).toHaveURL(/\/admin\/dashboard$/)
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
  await expect(page.getByText('E2E Admin Barber')).toBeVisible()
})

test('shows an error message for wrong credentials', async ({ page }) => {
  await page.goto('/admin/login')
  await page.getByPlaceholder('Email').fill('nobody@example.com')
  await page.getByPlaceholder('Password').fill('wrong-password')
  await page.getByRole('button', { name: 'Log In' }).click()

  await expect(page.getByText('Invalid email or password')).toBeVisible()
  await expect(page).toHaveURL(/\/admin\/login$/)
})
