import { createContext, useContext, useState, type ReactNode } from 'react'
import { customerApi } from './customerApi'

interface CustomerUser { id: string; name: string; familyName: string; phone: string }
interface CustomerAuthCtx {
  user: CustomerUser | null
  requestOtp: (phone: string) => Promise<{ isNewCustomer: boolean; devOtp?: string }>
  verifyOtp: (phone: string, otp: string, name?: string, familyName?: string) => Promise<void>
  logout: () => void
  isAuthenticated: boolean
  language: string
  setLang: (l: string) => void
}

const CustomerAuthContext = createContext<CustomerAuthCtx>(null!)

export function CustomerAuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CustomerUser | null>(() => {
    try { return JSON.parse(localStorage.getItem('customerUser') ?? 'null') }
    catch { return null }
  })
  // Independent from the barber admin's language (useAuth) and from any specific barber's
  // configured language — this is the customer's own choice, defaulting to Hebrew when unset.
  const [language, setLanguage] = useState(() => localStorage.getItem('customerLang') ?? 'HE')

  function setLang(l: string) {
    localStorage.setItem('customerLang', l)
    setLanguage(l)
  }

  async function requestOtp(phone: string) {
    const { data } = await customerApi.post('/customer/auth/otp', { phone })
    return data
  }

  async function verifyOtp(phone: string, otp: string, name?: string, familyName?: string) {
    const { data } = await customerApi.post('/customer/auth/verify', { phone, otp, name, familyName })
    localStorage.setItem('customerToken', data.token)
    const u: CustomerUser = { id: data.customerId, name: data.name, familyName: data.familyName, phone: data.phone }
    localStorage.setItem('customerUser', JSON.stringify(u))
    setUser(u)
  }

  function logout() {
    localStorage.removeItem('customerToken')
    localStorage.removeItem('customerUser')
    setUser(null)
  }

  return (
    <CustomerAuthContext.Provider value={{ user, requestOtp, verifyOtp, logout, isAuthenticated: !!user, language, setLang }}>
      {children}
    </CustomerAuthContext.Provider>
  )
}

export const useCustomerAuth = () => useContext(CustomerAuthContext)
