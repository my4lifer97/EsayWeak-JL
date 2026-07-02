import { Navigate, Outlet } from 'react-router-dom'
import { useCustomerAuth } from '../lib/customerAuth'

export default function CustomerProtectedRoute() {
  const { isAuthenticated } = useCustomerAuth()
  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />
}
