import { NavLink } from 'react-router-dom'
import { useAuth } from '../../lib/auth'
import { useNavigate } from 'react-router-dom'
import { t, type TKey } from '../../lib/i18n'
import ThemeToggle from '../ThemeToggle'

const NAV: { to: string; key: TKey; icon: string }[] = [
  { to: '/admin/dashboard', key: 'dashboard', icon: '📅' },
  { to: '/admin/appointments', key: 'appointments', icon: '📋' },
  { to: '/admin/recurring', key: 'recurringAppointments', icon: '🔁' },
  { to: '/admin/schedule', key: 'schedule', icon: '🕐' },
  { to: '/admin/services', key: 'services', icon: '✂️' },
  { to: '/admin/reviews', key: 'navReviews', icon: '★' },
  { to: '/admin/settings', key: 'settings', icon: '⚙️' },
]

function NavContent({ lang, onNavigate }: { lang: string; onNavigate?: () => void }) {
  return (
    <nav className="flex-1 space-y-1">
      {NAV.map((item) => (
        <NavLink
          key={item.to}
          to={item.to}
          onClick={onNavigate}
          className={({ isActive }) =>
            `flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition-colors ${
              isActive ? 'bg-coral text-white' : 'text-muted hover:text-ink hover:bg-cream'
            }`
          }
        >
          <span>{item.icon}</span>
          {t(lang, item.key)}
        </NavLink>
      ))}
    </nav>
  )
}

// Touch-primary devices (phones/tablets, see useIsTouchPrimary) get an off-canvas drawer;
// everything else -- any PC with a mouse, regardless of window width or browser zoom -- gets
// this exact always-visible static sidebar, unchanged from before mobile support existed.
function DesktopSidebar({ businessName }: { businessName: string }) {
  const { logout, language: lang } = useAuth()
  const navigate = useNavigate()

  function handleLogout() {
    logout()
    navigate('/admin/login')
  }

  return (
    <aside className="w-56 bg-surface border-e border-line flex flex-col py-6 px-3 shrink-0">
      <div className="px-3 mb-8 flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="text-ink font-bold text-lg">EsayWeek</div>
          <div className="text-muted text-sm mt-0.5 truncate">{businessName}</div>
        </div>
        <ThemeToggle />
      </div>
      <NavContent lang={lang} />
      <button
        onClick={handleLogout}
        className="flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-muted hover:text-ink hover:bg-cream transition-colors mt-4"
      >
        <span>🚪</span>
        {t(lang, 'signOut')}
      </button>
    </aside>
  )
}

function TouchSidebar({
  businessName, open, onClose,
}: { businessName: string; open: boolean; onClose: () => void }) {
  const { logout, language: lang } = useAuth()
  const navigate = useNavigate()

  function handleLogout() {
    logout()
    navigate('/admin/login')
  }

  return (
    <>
      {open && <div onClick={onClose} className="fixed inset-0 bg-black/60 z-40" />}
      <aside
        className={`w-56 bg-surface border-e border-line flex flex-col py-6 px-3 shrink-0
          fixed inset-y-0 start-0 z-50 transition-transform duration-200
          ${open ? 'translate-x-0' : 'rtl:translate-x-full -translate-x-full'}`}
      >
        <div className="px-3 mb-8 flex items-start justify-between gap-2">
          <div className="min-w-0">
            <div className="text-ink font-bold text-lg">EsayWeek</div>
            <div className="text-muted text-sm mt-0.5 truncate">{businessName}</div>
          </div>
          <div className="flex items-center gap-2">
            <ThemeToggle />
            <button onClick={onClose} aria-label="Close menu"
              className="text-muted hover:text-ink w-11 h-11 -m-2 flex items-center justify-center rounded-lg hover:bg-cream text-2xl leading-none transition-colors">✕</button>
          </div>
        </div>
        <NavContent lang={lang} onNavigate={onClose} />
        <button
          onClick={handleLogout}
          className="flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium text-muted hover:text-ink hover:bg-cream transition-colors mt-4"
        >
          <span>🚪</span>
          {t(lang, 'signOut')}
        </button>
      </aside>
    </>
  )
}

export default function AdminSidebar({
  businessName, isTouch, open, onClose,
}: { businessName: string; isTouch: boolean; open: boolean; onClose: () => void }) {
  return isTouch
    ? <TouchSidebar businessName={businessName} open={open} onClose={onClose} />
    : <DesktopSidebar businessName={businessName} />
}
