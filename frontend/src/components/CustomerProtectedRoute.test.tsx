import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import CustomerProtectedRoute from './CustomerProtectedRoute'
import { useCustomerAuth } from '../lib/customerAuth'

vi.mock('../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/login" element={<div>Customer Login Page</div>} />
        <Route element={<CustomerProtectedRoute />}>
          <Route path="/account/bookings" element={<div>My Bookings Page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  )
}

describe('CustomerProtectedRoute', () => {
  it('redirects to /login when not authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false } as ReturnType<typeof useCustomerAuth>)

    renderAt('/account/bookings')

    expect(screen.getByText('Customer Login Page')).toBeInTheDocument()
  })

  it('renders the nested route when authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: true } as ReturnType<typeof useCustomerAuth>)

    renderAt('/account/bookings')

    expect(screen.getByText('My Bookings Page')).toBeInTheDocument()
  })
})
