import { Link, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { useCustomerAuth } from '../../lib/customerAuth'
import { t } from '../../lib/i18n'

export default function CustomerAccountNav() {
  const { language: lang } = useAuth()
  const { isAuthenticated, logout } = useCustomerAuth()
  const location = useLocation()
  const navigate = useNavigate()

  const links = [
    { to: '/browse', key: 'navBrowse' as const },
    { to: '/account/bookings', key: 'navBookings' as const },
    { to: '/account/following', key: 'navFollowing' as const },
  ]

  function handleSignOut() {
    logout()
    navigate('/login')
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
            <Link to="/login" className="text-blue-400 hover:text-blue-300 text-sm transition-colors">{t(lang, 'loginTitle')}</Link>
          )}
        </div>
      </div>
    </nav>
  )
}
