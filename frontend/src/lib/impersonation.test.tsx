import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { clearImpersonationMarker, endImpersonation, getImpersonation } from './impersonation'
import { PlatformAdminAuthProvider, usePlatformAdminAuth } from './platformAdminAuth'

vi.mock('./platformAdminApi', () => ({
  platformAdminApi: { post: vi.fn() },
  PLATFORM_ADMIN_SESSION_EXPIRED: 'platform-admin-session-expired',
}))

function startImpersonatingBusiness() {
  localStorage.setItem('token', 'impersonated-owner-token')
  localStorage.setItem('user', '{"id":"b1"}')
  localStorage.setItem('impersonation', JSON.stringify({ type: 'business', name: 'Shop', returnPath: '/platform-admin/businesses/b1' }))
}

const originalLocation = window.location
beforeEach(() => {
  localStorage.clear()
  Object.defineProperty(window, 'location', { value: { href: '' }, writable: true })
})
afterEach(() => {
  Object.defineProperty(window, 'location', { value: originalLocation, writable: true })
})

describe('impersonation helpers', () => {
  it('endImpersonation removes the impersonated login and the marker', () => {
    startImpersonatingBusiness()
    localStorage.setItem('customerToken', 'real-customer-token')

    expect(endImpersonation()?.type).toBe('business')

    expect(localStorage.getItem('token')).toBeNull()
    expect(localStorage.getItem('user')).toBeNull()
    expect(getImpersonation()).toBeNull()
    expect(localStorage.getItem('customerToken')).toBe('real-customer-token')
  })

  it('clearImpersonationMarker only clears a marker of the same type', () => {
    startImpersonatingBusiness()

    clearImpersonationMarker('customer')
    expect(getImpersonation()).not.toBeNull()

    clearImpersonationMarker('business')
    expect(getImpersonation()).toBeNull()
  })
})

function AdminConsumer() {
  const { logout } = usePlatformAdminAuth()
  return <button onClick={logout}>admin-logout</button>
}

// QA: signing out of the platform admin left the "Log in as" owner session alive in the browser.
describe('platform admin logout', () => {
  it('ends an active impersonation and reloads to the admin login', async () => {
    localStorage.setItem('platformAdminUser', '{"id":"a1","name":"Admin","email":"a@example.com"}')
    startImpersonatingBusiness()
    render(<QueryClientProvider client={new QueryClient()}><PlatformAdminAuthProvider><AdminConsumer /></PlatformAdminAuthProvider></QueryClientProvider>)

    await userEvent.click(screen.getByText('admin-logout'))

    expect(localStorage.getItem('token')).toBeNull()
    expect(getImpersonation()).toBeNull()
    expect(window.location.href).toBe('/platform-admin/login')
  })

  it('leaves a real (non-impersonated) owner login alone', async () => {
    localStorage.setItem('platformAdminUser', '{"id":"a1","name":"Admin","email":"a@example.com"}')
    localStorage.setItem('token', 'real-owner-token')
    render(<QueryClientProvider client={new QueryClient()}><PlatformAdminAuthProvider><AdminConsumer /></PlatformAdminAuthProvider></QueryClientProvider>)

    await userEvent.click(screen.getByText('admin-logout'))

    expect(localStorage.getItem('token')).toBe('real-owner-token')
    expect(window.location.href).toBe('')
  })
})
