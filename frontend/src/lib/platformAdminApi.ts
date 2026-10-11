import axios from 'axios'

// See api.ts's comment: frontend and backend deploy as separate services with separate
// URLs in production, so the relative '/api' proxy that works in local dev doesn't exist
// in the built static output.
export const platformAdminApi = axios.create({ baseURL: `${import.meta.env.VITE_API_URL ?? ''}/api` })

platformAdminApi.interceptors.request.use((config) => {
  const token = localStorage.getItem('platformAdminToken')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// Fired on a 401 so PlatformAdminAuthProvider drops its logged-in state, which sends
// PlatformAdminProtectedRoute back to the login page instead of an empty dashboard.
export const PLATFORM_ADMIN_SESSION_EXPIRED = 'platform-admin-session-expired'

platformAdminApi.interceptors.response.use(
  (r) => r,
  (err) => {
    // A 401 from login itself is a wrong password, not an expired session.
    const isAuthRequest = /\/platform-admin\/(login|bootstrap)/.test(err.config?.url ?? '')
    if (err.response?.status === 401 && !isAuthRequest) {
      localStorage.removeItem('platformAdminToken')
      localStorage.removeItem('platformAdminUser')
      window.dispatchEvent(new Event(PLATFORM_ADMIN_SESSION_EXPIRED))
    }
    return Promise.reject(err)
  }
)
