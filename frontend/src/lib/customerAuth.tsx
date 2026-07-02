import { createContext, useContext, useState, type ReactNode } from 'react'
import { customerApi } from './customerApi'

interface CustomerUser { id: string; name: string; familyName: string; phone: string }
interface CustomerAuthCtx {
  user: CustomerUser | null
  requestOtp: (phone: string) => Promise<{ isNewCustomer: boolean; devOtp?: string }>
  verifyOtp: (phone: string, otp: string, name?: string, familyName?: string) => Promise<void>
  logout: () => void
  isAuthenticated: boolean
}

const CustomerAuthContext = createContext<CustomerAuthCtx>(null!)

export function CustomerAuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CustomerUser | null>(() => {
    try { return JSON.parse(localStorage.getItem('customerUser') ?? 'null') }
    catch { return null }
  })

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
    <CustomerAuthContext.Provider value={{ user, requestOtp, verifyOtp, logout, isAuthenticated: !!user }}>
      {children}
    </CustomerAuthContext.Provider>
  )
}

export const useCustomerAuth = () => useContext(CustomerAuthContext)
