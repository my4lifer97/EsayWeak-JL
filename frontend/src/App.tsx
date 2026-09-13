import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AuthProvider } from './lib/auth'
import { CustomerAuthProvider } from './lib/customerAuth'
import { PlatformAdminAuthProvider } from './lib/platformAdminAuth'

import LoginPage from './pages/admin/LoginPage'
import RegisterPage from './pages/admin/RegisterPage'
import ForgotPasswordPage from './pages/admin/ForgotPasswordPage'
import SetPasswordPage from './pages/admin/SetPasswordPage'
import DashboardPage from './pages/admin/DashboardPage'
import AppointmentsPage from './pages/admin/AppointmentsPage'
import RecurringAppointmentsPage from './pages/admin/RecurringAppointmentsPage'
import SchedulePage from './pages/admin/SchedulePage'
import ServicesPage from './pages/admin/ServicesPage'
import ReviewsPage from './pages/admin/ReviewsPage'
import SettingsPage from './pages/admin/SettingsPage'
import AdminLayout from './components/admin/AdminLayout'
import ProtectedRoute from './components/ProtectedRoute'
import CustomerProtectedRoute from './components/CustomerProtectedRoute'
import PlatformAdminProtectedRoute from './components/PlatformAdminProtectedRoute'
import ImpersonationBanner from './components/ImpersonationBanner'
import BusinessPage from './pages/public/BusinessPage'
import BookPage from './pages/public/BookPage'
import AppointmentPage from './pages/public/AppointmentPage'
import WhatsAppLandingPage from './pages/public/WhatsAppLandingPage'
import BrowseBusinessesPage from './pages/public/BrowseBusinessesPage'
import MyBookingsPage from './pages/public/MyBookingsPage'
import RequestBusinessAccountPage from './pages/public/RequestBusinessAccountPage'
import HomePage from './pages/HomePage'
import PlatformAdminLoginPage from './pages/platform-admin/LoginPage'
import PlatformAdminDashboardPage from './pages/platform-admin/DashboardPage'
import PlatformAdminBusinessDetailPage from './pages/platform-admin/BusinessDetailPage'
import PlatformAdminCustomerDetailPage from './pages/platform-admin/CustomerDetailPage'
import PlatformAdminRequestsPage from './pages/platform-admin/RequestsPage'

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
                <Route path="/request-business-account" element={<RequestBusinessAccountPage />} />
                <Route element={<ProtectedRoute />}>
                  {/* Outside AdminLayout deliberately -- a business with a pending temp password
                      would just see nav links to pages that all 403 until this is done. */}
                  <Route path="/admin/set-password" element={<SetPasswordPage />} />
                  <Route element={<AdminLayout />}>
                    <Route path="/admin/dashboard" element={<DashboardPage />} />
                    <Route path="/admin/appointments" element={<AppointmentsPage />} />
                    <Route path="/admin/recurring" element={<RecurringAppointmentsPage />} />
                    <Route path="/admin/schedule" element={<SchedulePage />} />
                    <Route path="/admin/services" element={<ServicesPage />} />
                    <Route path="/admin/reviews" element={<ReviewsPage />} />
                    <Route path="/admin/settings" element={<SettingsPage />} />
                  </Route>
                </Route>
                <Route path="/browse" element={<BrowseBusinessesPage />} />
                {/* Public, read-only storefront — a logged-out visitor who finds a business via
                    the directory can view it; booking still needs the WhatsApp-link session. */}
                <Route path="/:slug" element={<BusinessPage />} />
                <Route element={<CustomerProtectedRoute />}>
                  <Route path="/account/bookings" element={<MyBookingsPage />} />
                  <Route path="/:slug/book" element={<BookPage />} />
                </Route>
                {/* Magic-link view stays public and token-secured — opened directly from a
                    WhatsApp/SMS reminder, must not require login. */}
                <Route path="/:slug/appointments/:id" element={<AppointmentPage />} />
                {/* WhatsApp booking-link landing point — establishes the customer session itself
                    (see WhatsAppLandingPage), so it's public and outside CustomerProtectedRoute. */}
                <Route path="/:slug/w/:token" element={<WhatsAppLandingPage />} />
                {/* Deliberately not linked from any public nav -- internal tool only. */}
                <Route path="/platform-admin/login" element={<PlatformAdminLoginPage />} />
                <Route element={<PlatformAdminProtectedRoute />}>
                  <Route path="/platform-admin" element={<PlatformAdminDashboardPage />} />
                  <Route path="/platform-admin/businesses/:id" element={<PlatformAdminBusinessDetailPage />} />
                  <Route path="/platform-admin/customers/:id" element={<PlatformAdminCustomerDetailPage />} />
                  <Route path="/platform-admin/requests" element={<PlatformAdminRequestsPage />} />
                </Route>
              </Routes>
            </BrowserRouter>
          </PlatformAdminAuthProvider>
        </CustomerAuthProvider>
      </AuthProvider>
    </QueryClientProvider>
  )
}
