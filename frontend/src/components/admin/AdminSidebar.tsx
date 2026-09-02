import { NavLink } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { useNavigate } from 'react-router-dom'
import { t, type TKey } from '../../lib/i18n'

const NAV: { to: string; key: TKey; icon: string }[] = [
  { to: '/admin/dashboard', key: 'dashboard', icon: '📅' },
  { to: '/admin/appointments', key: 'appointments', icon: '📋' },
  { to: '/admin/recurring', key: 'recurringAppointments', icon: '🔁' },
  { to: '/admin/schedule', key: 'schedule', icon: '🕐' },
  { to: '/admin/services', key: 'services', icon: '✂️' },
  { to: '/admin/settings', key: 'settings', icon: '⚙️' },
]

export default function AdminSidebar({
  barberName, open, onClose,
}: { barberName: string; open: boolean; onClose: () => void }) {
  const { logout, language: lang } = useAuth()
  const navigate = useNavigate()

  function handleLogout() {
    logout()
    navigate('/admin/login')
  }

  return (
    <>
      {/* Backdrop is mobile-only (md:hidden) -- on desktop the sidebar is always visible
          inline, so there's nothing to dim behind it. */}
      {open && (
        <div onClick={onClose} className="fixed inset-0 bg-black/60 z-40 md:hidden" />
      )}
      <aside
        className={`w-56 bg-gray-900 border-e border-gray-800 flex flex-col py-6 px-3 shrink-0
          fixed inset-y-0 start-0 z-50 transition-transform duration-200 md:static md:translate-x-0 md:z-auto
          ${open ? 'translate-x-0' : 'rtl:translate-x-full -translate-x-full'}`}
      >
        <div className="px-3 mb-8 flex items-start justify-between">
          <div>
            <div className="text-white font-bold text-lg">EsayWeek</div>
            <div className="text-gray-400 text-sm mt-0.5 truncate">{barberName}</div>
          </div>
          <button onClick={onClose} className="text-gray-500 hover:text-white text-xl md:hidden" aria-label="Close menu">✕</button>
        </div>
        <nav className="flex-1 space-y-1">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              onClick={onClose}
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
    </>
  )
}
