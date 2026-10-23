import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useAuth } from '../lib/auth'

export default function ProtectedRoute() {
  const { isAuthenticated, user } = useAuth()
  const { pathname } = useLocation()
  if (!isAuthenticated) return <Navigate to="/admin/login" replace />
  // A temp password must be changed first. Sending them here up front, rather than on the 403 the
  // first request gets (api.ts), means no dashboard page mounts and fails a request (QA run 4).
  if (user?.mustChangePassword && pathname !== '/admin/set-password') return <Navigate to="/admin/set-password" replace />
  return <Outlet />
}
