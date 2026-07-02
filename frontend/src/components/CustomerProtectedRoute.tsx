import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { useCustomerAuth } from '../lib/customerAuth'

export default function CustomerProtectedRoute() {
  const { isAuthenticated } = useCustomerAuth()
  const location = useLocation()

  if (!isAuthenticated) {
    const next = encodeURIComponent(location.pathname + location.search)
    return <Navigate to={`/login?next=${next}`} replace />
  }

  return <Outlet />
}
