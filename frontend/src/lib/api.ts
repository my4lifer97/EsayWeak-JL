import axios from 'axios'

// Relative '/api' works in local dev via vite.config.ts's proxy, which doesn't exist in the
// built static output. In production, VITE_API_URL points at the backend's own domain since
// frontend and backend deploy as separate services with separate URLs.
export const api = axios.create({ baseURL: `${import.meta.env.VITE_API_URL ?? ''}/api` })

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('token')
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

api.interceptors.response.use(
  (r) => r,
  (err) => {
    // A 401 from /auth/login itself just means "wrong password" — not an expired session.
    // Redirecting here would hard-navigate away before LoginPage's own catch block can show
    // the error, wiping its React state via a full page reload.
    const isLoginRequest = err.config?.url?.includes('/auth/login')
    if (err.response?.status === 401 && !isLoginRequest) {
      localStorage.removeItem('token')
      localStorage.removeItem('user')
      window.location.href = '/admin/login'
    }
    // An admin-issued temp password that hasn't been replaced yet -- every BusinessOnly action
    // 403s with this flag except change-password itself. Catches a session left open in an old
    // tab from before the redirect below already happened, or reached without going through
    // LoginPage's own check.
    if (err.response?.status === 403 && err.response?.data?.mustChangePassword && window.location.pathname !== '/admin/set-password') {
      window.location.href = '/admin/set-password'
    }
    return Promise.reject(err)
  }
)
