import { createContext, useContext, useState, type ReactNode } from 'react'
import { customerApi } from './customerApi'

interface CustomerUser { id: string; name: string; familyName: string; phone: string }
interface WhatsAppLoginResult { barberSlug: string; serviceId: string }
interface CustomerAuthCtx {
  user: CustomerUser | null
  loginWithWhatsAppToken: (token: string) => Promise<WhatsAppLoginResult>
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

  // Redeems the opaque token from a WhatsApp-issued booking link (see WhatsAppLandingPage): no
  // sign-up/sign-in step, the customer's WhatsApp phone + profile name already identified them.
  async function loginWithWhatsAppToken(token: string) {
    const { data } = await customerApi.post('/customer/auth/whatsapp', { token })
    localStorage.setItem('customerToken', data.token)
    const u: CustomerUser = { id: data.customerId, name: data.name, familyName: data.familyName, phone: data.phone }
    localStorage.setItem('customerUser', JSON.stringify(u))
    setUser(u)
    // The backend detected this from the customer's own WhatsApp messages (see
    // WhatsAppController.DetectLanguage) -- carry it over so the wizard opens in the language
    // they were just chatting in, rather than whatever was last stored in this browser.
    if (data.language) setLang(data.language)
    return { barberSlug: data.barberSlug, serviceId: data.serviceId }
  }

  function logout() {
    localStorage.removeItem('customerToken')
    localStorage.removeItem('customerUser')
    setUser(null)
  }

  return (
    <CustomerAuthContext.Provider value={{ user, loginWithWhatsAppToken, logout, isAuthenticated: !!user, language, setLang }}>
      {children}
    </CustomerAuthContext.Provider>
  )
}

export const useCustomerAuth = () => useContext(CustomerAuthContext)
