import { createContext, useContext, useState, type ReactNode } from 'react'
import { platformAdminApi } from './platformAdminApi'

interface PlatformAdminUser { id: string; name: string; email: string }
interface PlatformAdminAuthCtx {
  user: PlatformAdminUser | null
  login: (email: string, password: string) => Promise<void>
  bootstrap: (email: string, password: string, name: string) => Promise<void>
  logout: () => void
  isAuthenticated: boolean
}

const PlatformAdminAuthContext = createContext<PlatformAdminAuthCtx>(null!)

export function PlatformAdminAuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<PlatformAdminUser | null>(() => {
    try { return JSON.parse(localStorage.getItem('platformAdminUser') ?? 'null') }
    catch { return null }
  })

  function persist(data: { token: string; id: string; name: string; email: string }) {
    localStorage.setItem('platformAdminToken', data.token)
    const u: PlatformAdminUser = { id: data.id, name: data.name, email: data.email }
    localStorage.setItem('platformAdminUser', JSON.stringify(u))
    setUser(u)
  }

  async function login(email: string, password: string) {
    const { data } = await platformAdminApi.post('/platform-admin/login', { email, password })
    persist(data)
  }

  async function bootstrap(email: string, password: string, name: string) {
    const { data } = await platformAdminApi.post('/platform-admin/bootstrap', { email, password, name })
    persist(data)
  }

  function logout() {
    localStorage.removeItem('platformAdminToken')
    localStorage.removeItem('platformAdminUser')
    setUser(null)
  }

  return (
    <PlatformAdminAuthContext.Provider value={{ user, login, bootstrap, logout, isAuthenticated: !!user }}>
      {children}
    </PlatformAdminAuthContext.Provider>
  )
}

export const usePlatformAdminAuth = () => useContext(PlatformAdminAuthContext)
