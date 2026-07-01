import { BrowserRouter, Routes, Route, Navigate, Outlet } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider, useAuth } from './lib/auth'

import LoginPage from './pages/admin/LoginPage'
import RegisterPage from './pages/admin/RegisterPage'
import DashboardPage from './pages/admin/DashboardPage'
import AppointmentsPage from './pages/admin/AppointmentsPage'
import SchedulePage from './pages/admin/SchedulePage'
import ServicesPage from './pages/admin/ServicesPage'
import SettingsPage from './pages/admin/SettingsPage'
import AdminLayout from './components/admin/AdminLayout'
import BarberPage from './pages/public/BarberPage'
import BookPage from './pages/public/BookPage'
import AppointmentPage from './pages/public/AppointmentPage'
import HomePage from './pages/HomePage'

const queryClient = new QueryClient()

function ProtectedRoute() {
  const { isAuthenticated } = useAuth()
  return isAuthenticated ? <Outlet /> : <Navigate to="/admin/login" replace />
}

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<HomePage />} />
            <Route path="/admin/login" element={<LoginPage />} />
            <Route path="/admin/register" element={<RegisterPage />} />
            <Route element={<ProtectedRoute />}>
              <Route element={<AdminLayout />}>
                <Route path="/admin/dashboard" element={<DashboardPage />} />
                <Route path="/admin/appointments" element={<AppointmentsPage />} />
                <Route path="/admin/schedule" element={<SchedulePage />} />
                <Route path="/admin/services" element={<ServicesPage />} />
                <Route path="/admin/settings" element={<SettingsPage />} />
              </Route>
            </Route>
            <Route path="/:slug" element={<BarberPage />} />
            <Route path="/:slug/book" element={<BookPage />} />
            <Route path="/:slug/appointments/:id" element={<AppointmentPage />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </QueryClientProvider>
  )
}
