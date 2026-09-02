import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import AdminSidebar from './AdminSidebar'
import { useAuth } from '../../lib/auth'

export default function AdminLayout() {
  const { user, language } = useAuth()
  const isRTL = language === 'AR' || language === 'HE'
  const [sidebarOpen, setSidebarOpen] = useState(false)
  return (
    <div className="flex h-screen bg-gray-950 overflow-hidden" dir={isRTL ? 'rtl' : 'ltr'}>
      <AdminSidebar barberName={user?.name ?? ''} open={sidebarOpen} onClose={() => setSidebarOpen(false)} />
      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        <div className="md:hidden flex items-center gap-3 px-4 py-3 border-b border-gray-800 bg-gray-900 shrink-0">
          <button onClick={() => setSidebarOpen(true)} className="text-gray-300 hover:text-white text-xl" aria-label="Open menu">☰</button>
          <div className="text-white font-bold">EsayWeek</div>
        </div>
        <main className="flex-1 overflow-y-auto p-4 md:p-8">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
