import { Suspense } from 'react'
import { createBrowserRouter, createRoutesFromElements, RouterProvider, Route, Navigate, Outlet } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ThemeProvider } from './lib/theme'
import { AuthProvider } from './lib/auth'
import { CustomerAuthProvider } from './lib/customerAuth'
import RouteErrorPage from './components/RouteErrorPage'
import { PlatformAdminAuthProvider } from './lib/platformAdminAuth'

import LoginPage from './pages/admin/LoginPage'
import RegisterPage from './pages/admin/RegisterPage'
import ForgotPasswordPage from './pages/admin/ForgotPasswordPage'
import SetPasswordPage from './pages/admin/SetPasswordPage'
import AdminLayout from './components/admin/AdminLayout'
import ProtectedRoute from './components/ProtectedRoute'
import CustomerProtectedRoute from './components/CustomerProtectedRoute'
import PlatformAdminProtectedRoute from './components/PlatformAdminProtectedRoute'
import ImpersonationBanner from './components/ImpersonationBanner'
import BusinessPage from './pages/public/BusinessPage'
import BookPage from './pages/public/BookPage'
import AppointmentPage from './pages/public/AppointmentPage'
import WhatsAppLandingPage from './pages/public/WhatsAppLandingPage'
import WhatsAppLinkPage from './pages/public/WhatsAppLinkPage'
import BrowseBusinessesPage from './pages/public/BrowseBusinessesPage'
import MyBookingsPage from './pages/public/MyBookingsPage'
import RequestBusinessAccountPage from './pages/public/RequestBusinessAccountPage'
import CustomerLoginPage from './pages/public/CustomerLoginPage'
import HomePage from './pages/HomePage'
import NotFoundPage from './pages/NotFoundPage'
import { lazyPage } from './lib/lazyPage'

// Owner dashboard and platform-admin pages load on demand (see lib/lazyPage).
const DashboardPage = lazyPage(() => import('./pages/admin/DashboardPage'))
const AppointmentsPage = lazyPage(() => import('./pages/admin/AppointmentsPage'))
const RecurringAppointmentsPage = lazyPage(() => import('./pages/admin/RecurringAppointmentsPage'))
const SchedulePage = lazyPage(() => import('./pages/admin/SchedulePage'))
const ServicesPage = lazyPage(() => import('./pages/admin/ServicesPage'))
const ReviewsPage = lazyPage(() => import('./pages/admin/ReviewsPage'))
const SettingsPage = lazyPage(() => import('./pages/admin/SettingsPage'))
const ChatbotSettingsPage = lazyPage(() => import('./pages/admin/ChatbotSettingsPage'))
const ChatsPage = lazyPage(() => import('./pages/admin/ChatsPage'))
const PlatformAdminLoginPage = lazyPage(() => import('./pages/platform-admin/LoginPage'))
const PlatformAdminDashboardPage = lazyPage(() => import('./pages/platform-admin/DashboardPage'))
const PlatformAdminBusinessDetailPage = lazyPage(() => import('./pages/platform-admin/BusinessDetailPage'))
const PlatformAdminCustomerDetailPage = lazyPage(() => import('./pages/platform-admin/CustomerDetailPage'))
const PlatformAdminRequestsPage = lazyPage(() => import('./pages/platform-admin/RequestsPage'))

// A 4xx (unknown slug, deleted record, no access) won't change on a retry -- the default 3 retries
// with backoff kept "Loading..." on screen ~8s before a not-found message (QA run 2).
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        const status = (error as { response?: { status?: number } })?.response?.status
        if (status !== undefined && status >= 400 && status < 500) return false
        return failureCount < 2
      },
    },
  },
})

// A data router (not <BrowserRouter>) so pages can use useBlocker -- the admin forms ask before
// leaving with unsaved edits (components/admin/useUnsavedChangesGuard).
const router = createBrowserRouter(createRoutesFromElements(
  <Route errorElement={<RouteErrorPage />} element={<><ImpersonationBanner /><Suspense fallback={null}><Outlet /></Suspense></>}>
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
        <Route path="/admin/chatbot" element={<ChatbotSettingsPage />} />
        <Route path="/admin/chats" element={<ChatsPage />} />
        <Route path="/admin/settings" element={<SettingsPage />} />
      </Route>
    </Route>
    {/* Second, parallel entry point alongside the WhatsApp booking-link flow --
        mainly for a future native mobile app, which can't rely on the customer
        having already messaged the business on WhatsApp. */}
    <Route path="/login" element={<CustomerLoginPage />} />
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
    {/* Shareable link a platform admin hands off to a business owner so they can scan
        their own WhatsApp-link QR code themselves -- see WhatsAppLinkController. */}
    <Route path="/wa-link/:token" element={<WhatsAppLinkPage />} />
    {/* Deliberately not linked from any public nav -- internal tool only. */}
    <Route path="/platform-admin/login" element={<PlatformAdminLoginPage />} />
    {/* Easy to type the words the other way round; it used to load as a business storefront and 404. */}
    <Route path="/admin-platform/*" element={<Navigate to="/platform-admin" replace />} />
    <Route element={<PlatformAdminProtectedRoute />}>
      <Route path="/platform-admin" element={<PlatformAdminDashboardPage />} />
      <Route path="/platform-admin/businesses/:id" element={<PlatformAdminBusinessDetailPage />} />
      <Route path="/platform-admin/customers/:id" element={<PlatformAdminCustomerDetailPage />} />
      <Route path="/platform-admin/requests" element={<PlatformAdminRequestsPage />} />
    </Route>
    <Route path="*" element={<NotFoundPage />} />
  </Route>
))

export default function App() {
  return (
    <ThemeProvider>
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <CustomerAuthProvider>
          <PlatformAdminAuthProvider>
            <RouterProvider router={router} />
          </PlatformAdminAuthProvider>
        </CustomerAuthProvider>
      </AuthProvider>
    </QueryClientProvider>
    </ThemeProvider>
  )
}
