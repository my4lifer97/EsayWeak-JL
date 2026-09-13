import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import CustomerProtectedRoute from './CustomerProtectedRoute'
import { useCustomerAuth } from '../lib/customerAuth'

vi.mock('../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

// Mirrors the real route layout in App.tsx: the public storefront `/:slug` sits OUTSIDE the
// guard, while booking (`/:slug/book`) and the account area stay inside it.
function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/:slug" element={<div>Business Page</div>} />
        <Route element={<CustomerProtectedRoute />}>
          <Route path="/account/bookings" element={<div>My Bookings Page</div>} />
          <Route path="/:slug/book" element={<div>Booking Page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  )
}

describe('CustomerProtectedRoute', () => {
  it('shows an inline message instead of the nested route when not authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderAt('/account/bookings')

    expect(screen.queryByText('My Bookings Page')).not.toBeInTheDocument()
  })

  it('renders the nested route when authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: true, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderAt('/account/bookings')

    expect(screen.getByText('My Bookings Page')).toBeInTheDocument()
  })

  it('gates the booking page (/:slug/book) when not authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderAt('/jamelmarie85/book')

    expect(screen.queryByText('Booking Page')).not.toBeInTheDocument()
  })

  it('leaves the public storefront (/:slug) ungated for anonymous visitors', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderAt('/jamelmarie85')

    expect(screen.getByText('Business Page')).toBeInTheDocument()
  })
})
