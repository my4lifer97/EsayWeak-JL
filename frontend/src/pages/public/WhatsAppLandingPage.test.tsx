import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import WhatsAppLandingPage from './WhatsAppLandingPage'
import { useCustomerAuth } from '../../lib/customerAuth'

vi.mock('../../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

function renderAt(path: string, loginWithWhatsAppToken: ReturnType<typeof vi.fn>) {
  vi.mocked(useCustomerAuth).mockReturnValue({
    loginWithWhatsAppToken, language: 'EN',
  } as unknown as ReturnType<typeof useCustomerAuth>)

  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/:slug/w/:token" element={<WhatsAppLandingPage />} />
        <Route path="/:slug/book" element={<div>Booking Wizard</div>} />
      </Routes>
    </MemoryRouter>
  )
}

beforeEach(() => vi.clearAllMocks())

describe('WhatsAppLandingPage', () => {
  it('redeems the token and redirects into the booking wizard with the service preselected', async () => {
    const login = vi.fn().mockResolvedValue({ barberSlug: 'test-barber', serviceId: 'svc-1' })

    renderAt('/test-barber/w/abc123', login)

    expect(login).toHaveBeenCalledWith('abc123')
    await waitFor(() => expect(screen.getByText('Booking Wizard')).toBeInTheDocument())
  })

  it('shows an expired-link message when the token is invalid', async () => {
    const login = vi.fn().mockRejectedValue(new Error('invalid'))

    renderAt('/test-barber/w/bad-token', login)

    await waitFor(() => expect(screen.getByText(/expired or is invalid/)).toBeInTheDocument())
  })
})
