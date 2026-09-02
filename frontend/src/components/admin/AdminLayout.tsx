import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import AdminSidebar from './AdminSidebar'
import { useAuth } from '../../lib/auth'
import { useIsTouchPrimary } from '../../lib/useIsTouchPrimary'

export default function AdminLayout() {
  const { user, language } = useAuth()
  const isRTL = language === 'AR' || language === 'HE'
  const isTouch = useIsTouchPrimary()
  const [sidebarOpen, setSidebarOpen] = useState(false)

  // Desktop: exact original layout (fixed-height shell, always-visible sidebar, independently
  // scrolling main) -- untouched regardless of window width or browser zoom, see useIsTouchPrimary.
  if (!isTouch) {
    return (
      <div className="flex h-screen bg-gray-950 overflow-hidden" dir={isRTL ? 'rtl' : 'ltr'}>
        <AdminSidebar barberName={user?.name ?? ''} isTouch={false} open={false} onClose={() => {}} />
        <main className="flex-1 overflow-y-auto p-8">
          <Outlet />
        </main>
      </div>
    )
  }

  // Touch (phone/tablet): plain page flow, no fixed-height/flex-grow shell -- a flex-1 item
  // inside an auto-height flex column is a known collapse trap (the container's height depends
  // on the item, the item's growth depends on the container), which was clipping page content
  // short and making the bottom-of-page button on every admin page unreachable. The header is
  // `sticky` so it stays pinned to the viewport top through the whole page's scroll.
  return (
    <div className="min-h-screen bg-gray-950" dir={isRTL ? 'rtl' : 'ltr'}>
      <AdminSidebar barberName={user?.name ?? ''} isTouch open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
      <div className="sticky top-0 z-30 flex items-center gap-3 px-4 py-3 border-b border-gray-800 bg-gray-900">
        <button onClick={() => setSidebarOpen(true)} className="text-gray-300 hover:text-white text-xl" aria-label="Open menu">☰</button>
        <div className="text-white font-bold">EsayWeek</div>
      </div>
      <main className="p-4">
        <Outlet />
      </main>
    </div>
  )
}
