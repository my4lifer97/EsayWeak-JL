import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import AdminSidebar from './AdminSidebar'
import { useAuth } from '../../lib/auth'

export default function AdminLayout() {
  const { user, language } = useAuth()
  const isRTL = language === 'AR' || language === 'HE'
  const [sidebarOpen, setSidebarOpen] = useState(false)
  return (
    // Below md this is a normal, page-scrolling layout (no h-screen/overflow-hidden) with a
    // `sticky` mobile header -- h-screen (100vh) is taller than what's actually visible on a
    // phone whenever the browser's address bar is showing, which made the *whole* flex column
    // scroll as one unit instead of just `main`, dragging the header (which was only `shrink-0`,
    // not pinned) off-screen while scrolling down a long page like Settings. `md:` and up keeps
    // the original desktop behavior exactly (fixed-height shell, sidebar + independently
    // scrolling main) unchanged.
    <div className="min-h-screen md:flex md:h-screen bg-gray-950 md:overflow-hidden" dir={isRTL ? 'rtl' : 'ltr'}>
      <AdminSidebar barberName={user?.name ?? ''} open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
      <div className="md:flex-1 flex flex-col min-w-0 md:overflow-hidden">
        <div className="md:hidden sticky top-0 z-30 flex items-center gap-3 px-4 py-3 border-b border-gray-800 bg-gray-900">
          <button onClick={() => setSidebarOpen(true)} className="text-gray-300 hover:text-white text-xl" aria-label="Open menu">☰</button>
          <div className="text-white font-bold">EsayWeek</div>
        </div>
        <main className="flex-1 md:overflow-y-auto p-4 md:p-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
