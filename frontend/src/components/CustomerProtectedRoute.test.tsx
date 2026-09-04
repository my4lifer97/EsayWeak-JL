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
        <Route element={<CustomerProtectedRoute />}>
          <Route path="/account/bookings" element={<div>My Bookings Page</div>} />
          <Route path="/:slug" element={<div>Barber Page</div>} />
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

  it('gates a barber page (/:slug) the same way when not authenticated', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderAt('/jamelmarie85')

    expect(screen.queryByText('Barber Page')).not.toBeInTheDocument()
  })
})
