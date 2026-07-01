import { Outlet } from 'react-router-dom'
import AdminSidebar from './AdminSidebar'
import { useAuth } from '../../lib/auth'

export default function AdminLayout() {
  const { user, language } = useAuth()
  const isRTL = language === 'AR' || language === 'HE'
  return (
    <div className="flex h-screen bg-gray-950 overflow-hidden" dir={isRTL ? 'rtl' : 'ltr'}>
      <AdminSidebar barberName={user?.name ?? ''} />
      <main className="flex-1 overflow-y-auto p-8">
        <Outlet />
      </main>
    </div>
  )
}
