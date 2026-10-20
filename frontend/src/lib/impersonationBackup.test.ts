import { beforeEach, describe, expect, it } from 'vitest'
import { clearImpersonationMarker, endImpersonation, getImpersonation, startImpersonation } from './impersonation'

// QA run 3 PA-11: "Log in as" overwrote, then deleted, an owner login the admin already had here.
describe('impersonation', () => {
  beforeEach(() => localStorage.clear())

  it('puts back the owner login this browser had when the impersonation ends', () => {
    localStorage.setItem('token', 'own-token')
    localStorage.setItem('user', '{"id":"own"}')

    startImpersonation({ type: 'business', name: 'Target', returnPath: '/platform-admin' }, { token: 'target-token', user: '{"id":"target"}' })
    expect(localStorage.getItem('token')).toBe('target-token')

    endImpersonation()
    expect(localStorage.getItem('token')).toBe('own-token')
    expect(localStorage.getItem('user')).toBe('{"id":"own"}')
    expect(getImpersonation()).toBeNull()
  })

  it('leaves the slots empty when there was no login before', () => {
    startImpersonation({ type: 'customer', name: 'C', returnPath: '/x' }, { customerToken: 't', customerUser: '{}' })
    clearImpersonationMarker('customer')
    expect(localStorage.getItem('customerToken')).toBe('t') // the session itself ended on its own; only the marker goes
    endImpersonation()
    expect(getImpersonation()).toBeNull()
  })

  it('keeps the original backup across a second "Log in as"', () => {
    localStorage.setItem('token', 'own-token')
    startImpersonation({ type: 'business', name: 'A', returnPath: '/a' }, { token: 'a' })
    startImpersonation({ type: 'business', name: 'B', returnPath: '/b' }, { token: 'b' })
    endImpersonation()
    expect(localStorage.getItem('token')).toBe('own-token')
  })

  it('ends a business impersonation before starting a customer one', () => {
    localStorage.setItem('token', 'own-token')
    startImpersonation({ type: 'business', name: 'A', returnPath: '/a' }, { token: 'a' })
    startImpersonation({ type: 'customer', name: 'C', returnPath: '/c' }, { customerToken: 'c' })
    expect(localStorage.getItem('token')).toBe('own-token')
    endImpersonation()
    expect(localStorage.getItem('customerToken')).toBeNull()
    expect(localStorage.getItem('token')).toBe('own-token')
  })
})
