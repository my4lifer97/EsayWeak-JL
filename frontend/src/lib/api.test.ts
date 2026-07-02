import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { api } from './api'

// axios doesn't expose a public API to invoke a registered interceptor directly, but the
// handler list on the InterceptorManager is stable and this is the standard lightweight way
// to unit-test interceptor logic without mocking a real HTTP transport.
function requestInterceptor() {
  return (api.interceptors.request as unknown as { handlers: { fulfilled: (c: unknown) => unknown }[] }).handlers[0].fulfilled
}
function responseRejected() {
  return (api.interceptors.response as unknown as { handlers: { rejected: (e: unknown) => unknown }[] }).handlers[0].rejected
}

describe('api request interceptor', () => {
  beforeEach(() => localStorage.clear())

  it('attaches the Bearer token when present in localStorage', () => {
    localStorage.setItem('token', 'abc123')
    const config = requestInterceptor()({ headers: {} }) as { headers: Record<string, string> }
    expect(config.headers.Authorization).toBe('Bearer abc123')
  })

  it('does not set an Authorization header when no token is stored', () => {
    const config = requestInterceptor()({ headers: {} }) as { headers: Record<string, string> }
    expect(config.headers.Authorization).toBeUndefined()
  })
})

describe('api response interceptor', () => {
  const originalLocation = window.location

  beforeEach(() => {
    localStorage.setItem('token', 'abc123')
    localStorage.setItem('user', '{"id":"1"}')
    Object.defineProperty(window, 'location', { value: { href: '' }, writable: true })
  })

  afterEach(() => {
    Object.defineProperty(window, 'location', { value: originalLocation, writable: true })
  })

  it('clears storage and redirects to /admin/login on a 401 from a protected endpoint', async () => {
    await expect(responseRejected()({ response: { status: 401 }, config: { url: '/admin/settings' } })).rejects.toBeTruthy()

    expect(localStorage.getItem('token')).toBeNull()
    expect(localStorage.getItem('user')).toBeNull()
    expect(window.location.href).toBe('/admin/login')
  })

  it('leaves storage untouched for non-401 errors', async () => {
    await expect(responseRejected()({ response: { status: 500 }, config: { url: '/admin/settings' } })).rejects.toBeTruthy()

    expect(localStorage.getItem('token')).toBe('abc123')
    expect(window.location.href).toBe('')
  })

  it('does not clear storage or redirect on a 401 from the login endpoint itself', async () => {
    // A wrong-password 401 on /auth/login is not a session expiry — redirecting here would
    // wipe LoginPage's own error message via a full page reload (see api.ts).
    await expect(responseRejected()({ response: { status: 401 }, config: { url: '/auth/login' } })).rejects.toBeTruthy()

    expect(localStorage.getItem('token')).toBe('abc123')
    expect(window.location.href).toBe('')
  })
})
