import { Outlet } from 'react-router-dom'
import { useCustomerAuth } from '../lib/customerAuth'
import { t } from '../lib/i18n'

// No manual sign-in exists anymore -- every customer session starts by redeeming a WhatsApp
// booking link (see WhatsAppLandingPage), which logs the customer in itself. So an
// unauthenticated visitor here (an expired/never-had session hitting a guarded route directly)
// has nowhere to "log in" to: this renders an inline message instead of redirecting to a login
// page, telling them to go back to WhatsApp for a fresh link.
export default function CustomerProtectedRoute() {
  const { isAuthenticated, language: lang } = useCustomerAuth()

  if (!isAuthenticated) {
    return (
      <div className="min-h-screen bg-gray-950 text-white flex items-center justify-center px-4">
        <p className="text-gray-400 text-center max-w-sm">{t(lang, 'whatsappLinkExpired')}</p>
      </div>
    )
  }

  return <Outlet />
}
