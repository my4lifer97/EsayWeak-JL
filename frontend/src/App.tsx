import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './lib/auth'
import { CustomerAuthProvider } from './lib/customerAuth'
import { PlatformAdminAuthProvider } from './lib/platformAdminAuth'

import LoginPage from './pages/admin/LoginPage'
import RegisterPage from './pages/admin/RegisterPage'
import ForgotPasswordPage from './pages/admin/ForgotPasswordPage'
import DashboardPage from './pages/admin/DashboardPage'
import AppointmentsPage from './pages/admin/AppointmentsPage'
import RecurringAppointmentsPage from './pages/admin/RecurringAppointmentsPage'
import SchedulePage from './pages/admin/SchedulePage'
import ServicesPage from './pages/admin/ServicesPage'
import SettingsPage from './pages/admin/SettingsPage'
import AdminLayout from './components/admin/AdminLayout'
import ProtectedRoute from './components/ProtectedRoute'
import CustomerProtectedRoute from './components/CustomerProtectedRoute'
import PlatformAdminProtectedRoute from './components/PlatformAdminProtectedRoute'
import ImpersonationBanner from './components/ImpersonationBanner'
import BarberPage from './pages/public/BarberPage'
import BookPage from './pages/public/BookPage'
import AppointmentPage from './pages/public/AppointmentPage'
import CustomerLoginPage from './pages/public/CustomerLoginPage'
import BrowseBarbersPage from './pages/public/BrowseBarbersPage'
import MyBookingsPage from './pages/public/MyBookingsPage'
import HomePage from './pages/HomePage'
import PlatformAdminLoginPage from './pages/platform-admin/LoginPage'
import PlatformAdminDashboardPage from './pages/platform-admin/DashboardPage'
import PlatformAdminBarberDetailPage from './pages/platform-admin/BarberDetailPage'
import PlatformAdminCustomerDetailPage from './pages/platform-admin/CustomerDetailPage'

const queryClient = new QueryClient()

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <CustomerAuthProvider>
          <PlatformAdminAuthProvider>
            <BrowserRouter>
              <ImpersonationBanner />
              <Routes>
                <Route path="/" element={<HomePage />} />
                <Route path="/admin/login" element={<LoginPage />} />
                <Route path="/admin/register" element={<RegisterPage />} />
                <Route path="/admin/forgot-password" element={<ForgotPasswordPage />} />
                <Route element={<ProtectedRoute />}>
                  <Route element={<AdminLayout />}>
                    <Route path="/admin/dashboard" element={<DashboardPage />} />
                    <Route path="/admin/appointments" element={<AppointmentsPage />} />
                    <Route path="/admin/recurring" element={<RecurringAppointmentsPage />} />
                    <Route path="/admin/schedule" element={<SchedulePage />} />
                    <Route path="/admin/services" element={<ServicesPage />} />
                    <Route path="/admin/settings" element={<SettingsPage />} />
                  </Route>
                </Route>
                <Route path="/login" element={<CustomerLoginPage />} />
                <Route path="/browse" element={<BrowseBarbersPage />} />
                <Route element={<CustomerProtectedRoute />}>
                  <Route path="/account/bookings" element={<MyBookingsPage />} />
                  <Route path="/:slug" element={<BarberPage />} />
                  <Route path="/:slug/book" element={<BookPage />} />
                </Route>
                {/* Magic-link view stays public and token-secured — opened directly from a
                    WhatsApp/SMS reminder, must not require login. */}
                <Route path="/:slug/appointments/:id" element={<AppointmentPage />} />
                {/* Deliberately not linked from any public nav -- internal tool only. */}
                <Route path="/platform-admin/login" element={<PlatformAdminLoginPage />} />
                <Route element={<PlatformAdminProtectedRoute />}>
                  <Route path="/platform-admin" element={<PlatformAdminDashboardPage />} />
                  <Route path="/platform-admin/barbers/:id" element={<PlatformAdminBarberDetailPage />} />
                  <Route path="/platform-admin/customers/:id" element={<PlatformAdminCustomerDetailPage />} />
                </Route>
              </Routes>
            </BrowserRouter>
          </PlatformAdminAuthProvider>
        </CustomerAuthProvider>
      </AuthProvider>
    </QueryClientProvider>
  )
}
