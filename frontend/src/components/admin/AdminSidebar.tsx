import { NavLink } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { useNavigate } from 'react-router-dom'
import { t, type TKey } from '../../lib/i18n'

const NAV: { to: string; key: TKey; icon: string }[] = [
  { to: '/admin/dashboard', key: 'dashboard', icon: '📅' },
  { to: '/admin/appointments', key: 'appointments', icon: '📋' },
  { to: '/admin/schedule', key: 'schedule', icon: '🕐' },
  { to: '/admin/services', key: 'services', icon: '✂️' },
  { to: '/admin/settings', key: 'settings', icon: '⚙️' },
]

export default function AdminSidebar({ barberName }: { barberName: string }) {
  const { logout, language: lang } = useAuth()
  const navigate = useNavigate()

  function handleLogout() {
    logout()
    navigate('/admin/login')
  }

  return (
    <aside className="w-56 bg-gray-900 border-e border-gray-800 flex flex-col py-6 px-3 shrink-0">
      <div className="px-3 mb-8">
        <div className="text-white font-bold text-lg">BarberBook</div>
        <div className="text-gray-400 text-sm mt-0.5 truncate">{barberName}</div>
      </div>
      <nav className="flex-1 space-y-1">
        {NAV.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              `flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
                isActive ? 'bg-blue-600 text-white' : 'text-gray-400 hover:text-white hover:bg-gray-800'
              }`
            }
          >
            <span>{item.icon}</span>
            {t(lang, item.key)}
          </NavLink>
        ))}
      </nav>
      <button
        onClick={handleLogout}
        className="flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-gray-400 hover:text-white hover:bg-gray-800 transition-colors mt-4"
      >
        <span>🚪</span>
        {t(lang, 'signOut')}
      </button>
    </aside>
  )
}
