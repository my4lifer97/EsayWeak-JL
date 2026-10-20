// A platform admin "logging in as" a business or customer stores that account's token in the
// normal owner/customer slots plus this marker (see BusinessDetailPage/CustomerDetailPage).
// Everything that ends a session goes through here, so the impersonated login never outlives the
// admin's own session and the yellow banner never outlives the impersonated login.
export type Impersonation = { type: 'business' | 'customer'; name: string; returnPath: string }

const KEY = 'impersonation'
// The owner/customer login this browser already had before "Log in as" replaced it, put back when
// the impersonation ends (QA run 3: it was overwritten, then deleted).
const BACKUP_KEY = 'impersonationBackup'
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

// Stores the target's login in the normal slots, keeping whatever was there to restore later.
export function startImpersonation(marker: Impersonation, session: Record<string, string>) {
  const keys = SESSION_KEYS[marker.type]
  // One impersonation at a time: switching from a business to a customer (or back) ends the first,
  // which puts its backup back. There's only one backup and one marker, so the first backup used
  // to be overwritten and the business slot kept the target's token with no banner.
  const current = getImpersonation()
  if (current && current.type !== marker.type) endImpersonation()
  // A second "Log in as" of the same type keeps the original backup, not the first target.
  if (getImpersonation()?.type !== marker.type) {
    const backup: Record<string, string> = {}
    keys.forEach((k) => { const v = localStorage.getItem(k); if (v !== null) backup[k] = v })
    localStorage.setItem(BACKUP_KEY, JSON.stringify({ type: marker.type, values: backup }))
  }
  Object.entries(session).forEach(([k, v]) => localStorage.setItem(k, v))
  localStorage.setItem(KEY, JSON.stringify(marker))
}

function restoreBackup(type: Impersonation['type']) {
  try {
    const raw = localStorage.getItem(BACKUP_KEY)
    const backup = raw ? JSON.parse(raw) as { type: string; values: Record<string, string> } : null
    if (backup?.type === type) Object.entries(backup.values).forEach(([k, v]) => localStorage.setItem(k, v))
    localStorage.removeItem(BACKUP_KEY)
  } catch { /* storage blocked or bad JSON -- nothing to restore */ }
}

// Removes the impersonated login itself plus the marker, putting back the login this browser had
// before; returns what was ended (null if none).
export function endImpersonation(): Impersonation | null {
  const current = getImpersonation()
  if (current) {
    SESSION_KEYS[current.type]?.forEach((k) => localStorage.removeItem(k))
    restoreBackup(current.type)
  }
  try { localStorage.removeItem(KEY) } catch { /* storage blocked -- nothing stored either */ }
  return current
}

// The owner/customer session of this type just ended on its own (logout, expired token) -- drop
// the marker if it belonged to it, so the banner doesn't stay up over a signed-out page.
export function clearImpersonationMarker(type: Impersonation['type']) {
  if (getImpersonation()?.type === type) {
    localStorage.removeItem(KEY)
    restoreBackup(type)
  }
}
