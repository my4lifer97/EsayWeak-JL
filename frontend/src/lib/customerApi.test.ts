import { describe, it, expect, beforeEach } from 'vitest'
import { customerApi } from './customerApi'

function requestInterceptor() {
  return (customerApi.interceptors.request as unknown as { handlers: { fulfilled: (c: unknown) => unknown }[] }).handlers[0].fulfilled
}
function responseRejected() {
  return (customerApi.interceptors.response as unknown as { handlers: { rejected: (e: unknown) => unknown }[] }).handlers[0].rejected
}

describe('customerApi request interceptor', () => {
  beforeEach(() => localStorage.clear())

  it('attaches the Bearer token when present in localStorage', () => {
    localStorage.setItem('customerToken', 'xyz789')
    const config = requestInterceptor()({ headers: {} }) as { headers: Record<string, string> }
    expect(config.headers.Authorization).toBe('Bearer xyz789')
  })

  it('does not set an Authorization header when no token is stored', () => {
    const config = requestInterceptor()({ headers: {} }) as { headers: Record<string, string> }
    expect(config.headers.Authorization).toBeUndefined()
  })
})

describe('customerApi response interceptor', () => {
  beforeEach(() => {
    localStorage.setItem('customerToken', 'xyz789')
    localStorage.setItem('customerUser', '{"id":"1"}')
  })

  it('clears customer storage on a 401 but does not hard-redirect', async () => {
    const originalHref = window.location.href

    await expect(responseRejected()({ response: { status: 401 } })).rejects.toBeTruthy()

    expect(localStorage.getItem('customerToken')).toBeNull()
    expect(localStorage.getItem('customerUser')).toBeNull()
    expect(window.location.href).toBe(originalHref)
  })

  it('leaves storage untouched for non-401 errors', async () => {
    await expect(responseRejected()({ response: { status: 500 } })).rejects.toBeTruthy()

    expect(localStorage.getItem('customerToken')).toBe('xyz789')
  })
})
