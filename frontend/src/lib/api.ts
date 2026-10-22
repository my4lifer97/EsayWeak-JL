import axios from 'axios'
import { clearImpersonationMarker } from './impersonation'

// Relative '/api' works in local dev via vite.config.ts's proxy, which doesn't exist in the
// built static output. In production, VITE_API_URL points at the backend's own domain since
// frontend and backend deploy as separate services with separate URLs.
export const api = axios.create({ baseURL: `${import.meta.env.VITE_API_URL ?? ''}/api` })

// The owner login this tab is using. localStorage is shared by every tab of the browser, so a
// "Log in as" or another login in a second tab used to switch this tab's lists and saves to that
// business without any sign of it (QA run 4). This tab keeps its own token until it reloads, and
// AuthProvider shows a notice when the stored one changes.
let tabToken: string | null = localStorage.getItem('token')

export function setOwnerTabToken(token: string | null) {
  tabToken = token
}

export function ownerSessionChangedElsewhere() {
  return tabToken !== null && localStorage.getItem('token') !== tabToken
}

api.interceptors.request.use((config) => {
  const token = tabToken ?? localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  // The backend translates its `error` messages to this language (TranslateErrorFilter). A call
  // made in another language (e.g. AppointmentPage, in the business's) passes its own header.
  if (!config.headers['X-Lang']) config.headers['X-Lang'] = localStorage.getItem('lang') ?? 'EN'
  return config
})

api.interceptors.response.use(
  (r) => r,
  (err) => {
    // A 401 from /auth/login itself just means "wrong password" — not an expired session.
    // Redirecting here would hard-navigate away before LoginPage's own catch block can show
    // the error, wiping its React state via a full page reload.
    const isLoginRequest = err.config?.url?.includes('/auth/login')
    // Another tab already replaced this tab's login: its error must not sign that tab out or
    // redirect. The notice asks this tab to reload instead.
    if (ownerSessionChangedElsewhere()) return Promise.reject(err)
    if (err.response?.status === 401 && !isLoginRequest) {
      localStorage.removeItem('token')
      localStorage.removeItem('user')
      tabToken = null
      // An expired "Log in as" session (60 min) -- don't leave the banner up.
      clearImpersonationMarker('business')
      window.location.href = '/admin/login'
    }
    // An admin-issued temp password that hasn't been replaced yet -- every BusinessOnly action
    // 403s with this flag except change-password itself. Catches a session left open in an old
    // tab from before the redirect below already happened, or reached without going through
    // LoginPage's own check.
    if (err.response?.status === 403 && err.response?.data?.mustChangePassword && window.location.pathname !== '/admin/set-password') {
      window.location.href = '/admin/set-password'
    }
    // The platform admin disabled this business (BlockDisabledBusinessFilter) -- end the session
    // and let the login page say why.
    if (err.response?.status === 403 && err.response?.data?.accountDisabled && !isLoginRequest) {
      localStorage.removeItem('token')
      localStorage.removeItem('user')
      tabToken = null
      window.location.href = '/admin/login?disabled=1'
    }
    return Promise.reject(err)
  }
)
