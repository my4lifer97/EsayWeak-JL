import { Navigate, Outlet } from 'react-router-dom'
import { usePlatformAdminAuth } from '../lib/platformAdminAuth'

export default function PlatformAdminProtectedRoute() {
  const { isAuthenticated } = usePlatformAdminAuth()

  if (!isAuthenticated) {
    return <Navigate to="/platform-admin/login" replace />
  }

  return <Outlet />
}
