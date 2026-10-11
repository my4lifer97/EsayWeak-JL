import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { api } from './api'
import { clearImpersonationMarker } from './impersonation'

interface User { id: string; name: string; email: string; slug: string; mustChangePassword: boolean }
interface AuthCtx {
  user: User | null
  login: (email: string, password: string) => Promise<User>
  verifyEmail: (email: string, code: string) => Promise<User>
  resendVerification: (email: string) => Promise<{ devCode?: string }>
  forgotPassword: (email: string) => Promise<{ devCode?: string }>
  resetPassword: (email: string, code: string, newPassword: string) => Promise<User>
  changePassword: (newPassword: string) => Promise<User>
  logout: () => void
  isAuthenticated: boolean
  language: string
  setLang: (l: string) => void
  updateUserName: (name: string) => void
}

const AuthContext = createContext<AuthCtx>(null!)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(() => {
    try { return JSON.parse(localStorage.getItem('user') ?? 'null') }
    catch { return null }
  })
  const [language, setLanguage] = useState(() => localStorage.getItem('lang') ?? 'EN')
  const queryClient = useQueryClient()

  function setLang(l: string) {
    localStorage.setItem('lang', l)
    setLanguage(l)
  }

  // The business's saved language previously only reached this context when SettingsPage
  // happened to be visited (it fetches /admin/settings and calls setLang itself) — every
  // other admin page fell back to whatever was last in localStorage (default English) until
  // then. Sync it on login and on session restore so it's correct everywhere from the start.
  useEffect(() => {
    if (!user) return
    api.get('/admin/settings').then(({ data }) => setLang(data.language)).catch(() => {})
  }, [user?.id])

  // Every query cached so far belongs to whoever was signed in before (or nobody) -- drop it, or
  // the next owner on a shared tablet sees the previous one's customers and Settings could save
  // the previous business's details into theirs.
  function storeSession(data: { token: string; id: string; name: string; email: string; slug: string; mustChangePassword?: boolean }) {
    queryClient.clear()
    localStorage.setItem('token', data.token)
    const u = { id: data.id, name: data.name, email: data.email, slug: data.slug, mustChangePassword: !!data.mustChangePassword }
    localStorage.setItem('user', JSON.stringify(u))
    setUser(u)
    return u
  }

  async function login(email: string, password: string) {
    const { data } = await api.post('/auth/login', { email, password })
    return storeSession(data)
  }

  async function verifyEmail(email: string, code: string) {
    const { data } = await api.post('/auth/verify-email', { email, code })
    return storeSession(data)
  }

  async function resendVerification(email: string) {
    const { data } = await api.post('/auth/resend-verification', { email })
    return data
  }

  async function forgotPassword(email: string) {
    const { data } = await api.post('/auth/forgot-password', { email })
    return data
  }

  async function resetPassword(email: string, code: string, newPassword: string) {
    const { data } = await api.post('/auth/reset-password', { email, code, newPassword })
    return storeSession(data)
  }

  // Used to clear a pending MustChangePassword (an admin-issued temp password) -- must remain
  // reachable while that lockout is in effect, see backend's AllowWithPendingPasswordChange.
  async function changePassword(newPassword: string) {
    const { data } = await api.patch('/auth/change-password', { newPassword })
    return storeSession(data)
  }

  function logout() {
    localStorage.removeItem('token')
    localStorage.removeItem('user')
    clearImpersonationMarker('business')
    queryClient.clear()
    setUser(null)
  }

  // The sidebar (AdminSidebar via AdminLayout) reads the business name from this context, which
  // is only ever set at login/verify/reset -- without this, renaming the business in Settings
  // saves correctly server-side but the sidebar keeps showing the old name until the next login.
  function updateUserName(name: string) {
    setUser((u) => {
      if (!u) return u
      const updated = { ...u, name }
      localStorage.setItem('user', JSON.stringify(updated))
      return updated
    })
  }

  return (
    <AuthContext.Provider value={{ user, login, verifyEmail, resendVerification, forgotPassword, resetPassword, changePassword, logout, isAuthenticated: !!user, language, setLang, updateUserName }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)
