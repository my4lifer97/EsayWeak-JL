import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'
import LanguageSwitcher from './LanguageSwitcher'

export default function CustomerAccountNav() {
  const { language: lang, isAuthenticated, logout } = useCustomerAuth()
  const location = useLocation()
  const navigate = useNavigate()

  const links = [
    { to: '/browse', key: 'navBrowse' as const },
    { to: '/account/bookings', key: 'navBookings' as const },
  ]

  function handleSignOut() {
    logout()
    // No manual sign-in page exists anymore -- a fresh session only starts from a WhatsApp
    // booking link, so there's nowhere to send them to log back in. Browse is still open to
    // anonymous visitors.
    navigate('/browse')
  }

  return (
    <nav className="border-b border-gray-900 px-4 py-3">
      <div className="max-w-2xl mx-auto flex items-center justify-between">
        <Link to="/browse" className="font-bold text-white">✂️</Link>
        <div className="flex items-center gap-4">
          {links.map((l) => (
            <Link key={l.to} to={l.to}
              className={`text-sm transition-colors ${location.pathname === l.to ? 'text-white font-medium' : 'text-gray-500 hover:text-gray-300'}`}>
              {t(lang, l.key)}
            </Link>
          ))}
          {isAuthenticated ? (
            <button onClick={handleSignOut} className="text-gray-500 hover:text-gray-300 text-sm transition-colors">
              {t(lang, 'signOutPortal')}
            </button>
          ) : (
            // No manual sign-in page exists anymore -- a session only starts from a WhatsApp
            // booking link, so there's nothing to link to here.
            <span className="text-gray-600 text-sm">{t(lang, 'whatsappOnlyAccess')}</span>
          )}
          <LanguageSwitcher />
        </div>
      </div>
    </nav>
  )
}
