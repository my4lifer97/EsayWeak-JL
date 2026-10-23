import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import ProtectedRoute from './ProtectedRoute'
import { useAuth } from '../lib/auth'

vi.mock('../lib/auth', () => ({
  useAuth: vi.fn(),
}))

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/admin/login" element={<div>Login Page</div>} />
        <Route element={<ProtectedRoute />}>
          <Route path="/admin/dashboard" element={<div>Dashboard Page</div>} />
          <Route path="/admin/set-password" element={<div>Set Password Page</div>} />
        </Route>
      </Routes>
    </MemoryRouter>
  )
}

describe('ProtectedRoute', () => {
  it('redirects to /admin/login when not authenticated', () => {
    vi.mocked(useAuth).mockReturnValue({ isAuthenticated: false } as ReturnType<typeof useAuth>)

    renderAt('/admin/dashboard')

    expect(screen.getByText('Login Page')).toBeInTheDocument()
  })

  it('renders the nested route when authenticated', () => {
    vi.mocked(useAuth).mockReturnValue({ isAuthenticated: true } as ReturnType<typeof useAuth>)

    renderAt('/admin/dashboard')

    expect(screen.getByText('Dashboard Page')).toBeInTheDocument()
  })

  it('sends a session with a temp password to set-password before any page mounts', () => {
    vi.mocked(useAuth).mockReturnValue({ isAuthenticated: true, user: { mustChangePassword: true } } as ReturnType<typeof useAuth>)

    renderAt('/admin/dashboard')

    expect(screen.getByText('Set Password Page')).toBeInTheDocument()
    expect(screen.queryByText('Dashboard Page')).not.toBeInTheDocument()
  })
})
