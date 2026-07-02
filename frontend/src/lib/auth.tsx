import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { api } from './api'

interface User { id: string; name: string; email: string; slug: string }
interface AuthCtx {
  user: User | null
  login: (email: string, password: string) => Promise<void>
  logout: () => void
  isAuthenticated: boolean
  language: string
  setLang: (l: string) => void
}

const AuthContext = createContext<AuthCtx>(null!)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(() => {
    try { return JSON.parse(localStorage.getItem('user') ?? 'null') }
    catch { return null }
  })
  const [language, setLanguage] = useState(() => localStorage.getItem('lang') ?? 'EN')

  function setLang(l: string) {
    localStorage.setItem('lang', l)
    setLanguage(l)
  }

  // The barber's saved language previously only reached this context when SettingsPage
  // happened to be visited (it fetches /admin/settings and calls setLang itself) — every
  // other admin page fell back to whatever was last in localStorage (default English) until
  // then. Sync it on login and on session restore so it's correct everywhere from the start.
  useEffect(() => {
    if (!user) return
    api.get('/admin/settings').then(({ data }) => setLang(data.language)).catch(() => {})
  }, [user?.id])

  async function login(email: string, password: string) {
    const { data } = await api.post('/auth/login', { email, password })
    localStorage.setItem('token', data.token)
    const u = { id: data.id, name: data.name, email: data.email, slug: data.slug }
    localStorage.setItem('user', JSON.stringify(u))
    setUser(u)
  }

  function logout() {
    localStorage.removeItem('token')
    localStorage.removeItem('user')
    setUser(null)
  }

  return (
    <AuthContext.Provider value={{ user, login, logout, isAuthenticated: !!user, language, setLang }}>
      {children}
    </AuthContext.Provider>
  )
}

export const useAuth = () => useContext(AuthContext)
