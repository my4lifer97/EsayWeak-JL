import { useEffect } from 'react'
import { useLocation, useSearchParams } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { useCustomerAuth } from '../lib/customerAuth'

const OWNER_PATHS = ['/admin', '/request-business-account']
const LEGAL_PATHS = ['/terms', '/privacy', '/accessibility']

// Keeps <html lang dir> on the language the page is shown in, for screen readers (QA run 11: it
// was always "en"). Pages still set dir on their own root.
export default function DocumentLanguage() {
  const { pathname } = useLocation()
  const [searchParams] = useSearchParams()
  const { language: ownerLang } = useAuth()
  const { language: customerLang } = useCustomerAuth()

  const asked = LEGAL_PATHS.includes(pathname) ? searchParams.get('lang')?.toUpperCase() : undefined
  const lang = pathname.startsWith('/platform-admin') ? 'EN'
    : OWNER_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`)) ? ownerLang
    : asked === 'HE' || asked === 'AR' || asked === 'EN' ? asked
    : customerLang

  useEffect(() => {
    document.documentElement.lang = lang.toLowerCase()
    document.documentElement.dir = lang === 'HE' || lang === 'AR' ? 'rtl' : 'ltr'
  }, [lang])
  return null
}
