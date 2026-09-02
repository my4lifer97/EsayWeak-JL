// Uploaded photo/logo URLs come back from the API as relative paths (e.g. "/api/uploads/...")
// because the backend has no idea what domain it's served behind. That's fine when frontend and
// backend share an origin (local dev, proxied), but in production they're separate Railway
// services with separate domains -- a relative src resolves against the *frontend's* origin,
// which 404s and renders as a broken-image icon. Prefix with VITE_API_URL (same var api.ts uses)
// so these always point at the backend regardless of where the page is served from.
export function mediaUrl(path: string | null | undefined): string {
  if (!path) return ''
  if (/^(https?:|blob:|data:)/.test(path)) return path
  return `${import.meta.env.VITE_API_URL ?? ''}${path}`
}
