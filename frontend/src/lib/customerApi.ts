import axios from 'axios'

// See api.ts's comment: frontend and backend deploy as separate services with separate
// URLs in production, so the relative '/api' proxy that works in local dev doesn't exist
// in the built static output.
export const customerApi = axios.create({ baseURL: `${import.meta.env.VITE_API_URL ?? ''}/api` })

customerApi.interceptors.request.use((config) => {
  const token = localStorage.getItem('customerToken')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

// Fired on a 401 so CustomerAuthProvider drops its logged-in state too -- clearing storage alone
// left "Sign Out" in the menu and pages quietly loading as empty after the token expired.
export const CUSTOMER_SESSION_EXPIRED = 'customer-session-expired'

customerApi.interceptors.response.use(
  (r) => r,
  (err) => {
    // The login endpoints' own 401s are wrong codes, not an expired session.
    const isAuthRequest = err.config?.url?.includes('/customer/auth/')
    if (err.response?.status === 401 && !isAuthRequest) {
      localStorage.removeItem('customerToken')
      localStorage.removeItem('customerUser')
      window.dispatchEvent(new Event(CUSTOMER_SESSION_EXPIRED))
    }
    return Promise.reject(err)
  }
)
