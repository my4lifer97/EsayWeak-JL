import { createContext, useContext, useEffect, useState, type ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { platformAdminApi, PLATFORM_ADMIN_SESSION_EXPIRED } from './platformAdminApi'
import { endImpersonation } from './impersonation'

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
  const queryClient = useQueryClient()

  // platformAdminApi got a 401 (the 12h login expired) -- back to the login page, not an empty dashboard.
  useEffect(() => {
    const onExpired = () => {
      queryClient.clear()
      setUser(null)
      if (endImpersonation()) window.location.href = '/platform-admin/login'
    }
    window.addEventListener(PLATFORM_ADMIN_SESSION_EXPIRED, onExpired)
    return () => window.removeEventListener(PLATFORM_ADMIN_SESSION_EXPIRED, onExpired)
  }, [queryClient])

  function persist(data: { token: string; id: string; name: string; email: string }) {
    queryClient.clear()
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
    queryClient.clear()
    setUser(null)
    // An owner/customer login opened via "Log in as" must not outlive the admin's own session.
    // Full reload so the owner/customer auth providers drop it from memory too.
    if (endImpersonation()) window.location.href = '/platform-admin/login'
  }

  return (
    <PlatformAdminAuthContext.Provider value={{ user, login, bootstrap, logout, isAuthenticated: !!user }}>
      {children}
    </PlatformAdminAuthContext.Provider>
  )
}

export const usePlatformAdminAuth = () => useContext(PlatformAdminAuthContext)
