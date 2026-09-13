import { createContext, useContext, useState, type ReactNode } from 'react'
import { customerApi } from './customerApi'

interface CustomerUser { id: string; name: string; familyName: string; phone: string }
interface WhatsAppLoginResult { businessSlug: string; itemId: string }
interface CustomerAuthCtx {
  user: CustomerUser | null
  loginWithWhatsAppToken: (token: string) => Promise<WhatsAppLoginResult>
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
  // Independent from the business admin's language (useAuth) and from any specific business's
  // configured language — this is the customer's own choice, defaulting to Hebrew when unset.
  const [language, setLanguage] = useState(() => localStorage.getItem('customerLang') ?? 'HE')

  function setLang(l: string) {
    localStorage.setItem('customerLang', l)
    setLanguage(l)
  }

  // Shared by every login path below -- each just needs to get a token + the four CustomerUser
  // fields from its own endpoint shape, then land here.
  function storeSession(data: { token: string; customerId: string; name: string; familyName: string; phone: string }) {
    localStorage.setItem('customerToken', data.token)
    const u: CustomerUser = { id: data.customerId, name: data.name, familyName: data.familyName, phone: data.phone }
    localStorage.setItem('customerUser', JSON.stringify(u))
    setUser(u)
  }

  // Redeems the opaque token from a WhatsApp-issued booking link (see WhatsAppLandingPage): no
  // sign-up/sign-in step, the customer's WhatsApp phone + profile name already identified them.
  async function loginWithWhatsAppToken(token: string) {
    const { data } = await customerApi.post('/customer/auth/whatsapp', { token })
    storeSession(data)
    // The backend detected this from the customer's own WhatsApp messages (see
    // WhatsAppController.DetectLanguage) -- carry it over so the wizard opens in the language
    // they were just chatting in, rather than whatever was last stored in this browser.
    if (data.language) setLang(data.language)
    return { businessSlug: data.businessSlug, itemId: data.itemId }
  }

  // Direct phone+OTP login -- a second, parallel entry point alongside WhatsApp (mainly for a
  // future native mobile app, which can't rely on the customer having already messaged the
  // business on WhatsApp).
  async function requestOtp(phone: string) {
    const { data } = await customerApi.post('/customer/auth/otp', { phone })
    return data
  }

  async function verifyOtp(phone: string, otp: string, name?: string, familyName?: string) {
    const { data } = await customerApi.post('/customer/auth/verify', { phone, otp, name, familyName })
    storeSession(data)
  }

  function logout() {
    localStorage.removeItem('customerToken')
    localStorage.removeItem('customerUser')
    setUser(null)
  }

  return (
    <CustomerAuthContext.Provider value={{ user, loginWithWhatsAppToken, requestOtp, verifyOtp, logout, isAuthenticated: !!user, language, setLang }}>
      {children}
    </CustomerAuthContext.Provider>
  )
}

export const useCustomerAuth = () => useContext(CustomerAuthContext)
