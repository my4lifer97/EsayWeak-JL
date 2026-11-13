import { useEffect } from 'react'
import { useLocation, useSearchParams } from 'react-router-dom'
import { useAuth } from '../lib/auth'
import { useCustomerAuth } from '../lib/customerAuth'
import { t, type TKey } from '../lib/i18n'
import { pageTitle } from '../lib/documentTitle'

const OWNER_PATHS = ['/admin', '/request-business-account']
const LEGAL_PATHS = ['/terms', '/privacy', '/accessibility']

// The tab title of each fixed page, in the page's language.
const TITLES: Record<string, TKey> = {
  '/admin/login': 'authSignIn',
  '/admin/register': 'authCreateAccount',
  '/admin/forgot-password': 'authResetTitle',
  '/admin/set-password': 'authSetPasswordTitle',
  '/request-business-account': 'reqTitle',
  '/admin/dashboard': 'dashboard',
  '/admin/appointments': 'appointments',
  '/admin/recurring': 'recurringAppointments',
  '/admin/schedule': 'schedule',
  '/admin/services': 'services',
  '/admin/reviews': 'navReviews',
  '/admin/chats': 'navChats',
  '/admin/chatbot': 'chatbotSettings',
  '/admin/settings': 'settings',
  '/login': 'loginTitle',
  '/browse': 'browseBusinesses',
  '/account/bookings': 'myBookingsTitle',
}
// Pages that set their own title: a business's page and appointment (its name, through
// useDocumentTitle) and the legal pages (their heading).
const OWN_TITLE = [/^\/[^/]+$/, /^\/[^/]+\/appointments\/[^/]+$/]
// Booking: BookPage puts the business name on it after this runs; signed out, the sign-in
// notice shows instead of BookPage and this one stays.
const BOOK_PATH = /^\/[^/]+\/book$/

// Keeps <html lang dir> on the language the page is shown in, for screen readers (QA run 11: it
// was always "en"), and the tab title on the page's name. Pages still set dir on their own root.
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

  useEffect(() => {
    const key = TITLES[pathname]
    if (key) document.title = pageTitle(t(lang, key))
    else if (pathname.startsWith('/platform-admin')) document.title = pageTitle('Platform admin')
    else if (BOOK_PATH.test(pathname)) document.title = pageTitle(t(lang, 'bookAppointment'))
    else if (!LEGAL_PATHS.includes(pathname) && !OWN_TITLE.some((r) => r.test(pathname))) document.title = pageTitle()
  }, [pathname, lang])
  return null
}
