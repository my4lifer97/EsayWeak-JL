import { Outlet, Link, useLocation } from 'react-router-dom'
import { useCustomerAuth } from '../lib/customerAuth'
import { t } from '../lib/i18n'

// A customer session starts either by redeeming a WhatsApp booking link (see
// WhatsAppLandingPage) or by signing in directly with phone+OTP (see CustomerLoginPage) -- an
// unauthenticated visitor here (an expired/never-had session hitting a guarded route directly)
// is offered both options rather than a redirect, since there's no single canonical login page
// the way business/platform-admin auth has.
export default function CustomerProtectedRoute() {
  const { isAuthenticated, language: lang } = useCustomerAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    return (
      <div className="min-h-screen bg-gray-950 text-white flex items-center justify-center px-4">
        <div className="text-center max-w-sm space-y-4">
          <p className="text-gray-400">{t(lang, 'whatsappLinkExpired')}</p>
          <Link
            to={`/login?next=${encodeURIComponent(location.pathname)}`}
            className="inline-block bg-blue-600 hover:bg-blue-700 text-white font-semibold px-5 py-2.5 rounded-xl transition-colors"
          >
            {t(lang, 'navLogin')}
          </Link>
        </div>
      </div>
    )
  }

  return <Outlet />
}
