// A platform admin "logging in as" a business or customer stores that account's token in the
// normal owner/customer slots plus this marker (see BusinessDetailPage/CustomerDetailPage).
// Everything that ends a session goes through here, so the impersonated login never outlives the
// admin's own session and the yellow banner never outlives the impersonated login.
export type Impersonation = { type: 'business' | 'customer'; name: string; returnPath: string }

const KEY = 'impersonation'
const SESSION_KEYS = {
  business: ['token', 'user'],
  customer: ['customerToken', 'customerUser'],
} as const

export function getImpersonation(): Impersonation | null {
  try {
    const raw = localStorage.getItem(KEY)
    return raw ? JSON.parse(raw) : null
  } catch {
    return null
  }
}

// Removes the impersonated login itself plus the marker; returns what was ended (null if none).
export function endImpersonation(): Impersonation | null {
  const current = getImpersonation()
  if (current) SESSION_KEYS[current.type]?.forEach((k) => localStorage.removeItem(k))
  try { localStorage.removeItem(KEY) } catch { /* storage blocked -- nothing stored either */ }
  return current
}

// The owner/customer session of this type just ended on its own (logout, expired token) -- drop
// the marker if it belonged to it, so the banner doesn't stay up over a signed-out page.
export function clearImpersonationMarker(type: Impersonation['type']) {
  if (getImpersonation()?.type === type) localStorage.removeItem(KEY)
}
