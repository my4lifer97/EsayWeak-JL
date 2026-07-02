import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route, useSearchParams } from 'react-router-dom'
import CustomerProtectedRoute from './CustomerProtectedRoute'
import { useCustomerAuth } from '../lib/customerAuth'

vi.mock('../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

function LoginProbe() {
  const [params] = useSearchParams()
  return <div>Customer Login Page (next={params.get('next')})</div>
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/login" element={<LoginProbe />} />
        <Route element={<CustomerProtectedRoute />}>
          <Route path="/account/bookings" element={<div>My Bookings Page</div>} />
          <Route path="/:slug" element={<div>Barber Page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  )
}

describe('CustomerProtectedRoute', () => {
  it('redirects to /login when not authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false } as ReturnType<typeof useCustomerAuth>)

    renderAt('/account/bookings')

    expect(screen.getByText(/Customer Login Page/)).toBeInTheDocument()
  })

  it('renders the nested route when authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: true } as ReturnType<typeof useCustomerAuth>)

    renderAt('/account/bookings')

    expect(screen.getByText('My Bookings Page')).toBeInTheDocument()
  })

  it('preserves the attempted path as ?next= so login can return the user there', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false } as ReturnType<typeof useCustomerAuth>)

    renderAt('/account/bookings')

    expect(screen.getByText('Customer Login Page (next=/account/bookings)')).toBeInTheDocument()
  })

  it('gates a barber page (/:slug) the same way, redirecting with next set to that slug', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false } as ReturnType<typeof useCustomerAuth>)

    renderAt('/jamelmarie85')

    expect(screen.getByText('Customer Login Page (next=/jamelmarie85)')).toBeInTheDocument()
  })
})
