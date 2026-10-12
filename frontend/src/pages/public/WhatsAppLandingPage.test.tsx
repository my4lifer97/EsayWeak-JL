import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import WhatsAppLandingPage from './WhatsAppLandingPage'
import { useCustomerAuth } from '../../lib/customerAuth'

vi.mock('../../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

const setLang = vi.fn()

function renderAt(path: string, loginWithWhatsAppToken: ReturnType<typeof vi.fn>) {
  vi.mocked(useCustomerAuth).mockReturnValue({
    loginWithWhatsAppToken, language: 'EN', setLang,
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

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
})

describe('WhatsAppLandingPage', () => {
  it('redeems the token and redirects into the booking wizard with the service preselected', async () => {
    const login = vi.fn().mockResolvedValue({ businessSlug: 'test-business', itemId: 'svc-1' })

    renderAt('/test-business/w/abc123', login)

    expect(login).toHaveBeenCalledWith('abc123')
    await waitFor(() => expect(screen.getByText('Booking Wizard')).toBeInTheDocument())
  })

  it('shows an expired-link message when the token is invalid', async () => {
    const login = vi.fn().mockRejectedValue(new Error('invalid'))

    renderAt('/test-business/w/bad-token', login)

    await waitFor(() => expect(screen.getByText(/expired or is invalid/)).toBeInTheDocument())
  })

  // QA: the expired page was always Hebrew, whatever language the customer chatted in.
  it('switches to the chat language the server returns for an expired link', async () => {
    const login = vi.fn().mockRejectedValue({ response: { status: 400, data: { error: 'expired', language: 'AR' } } })

    renderAt('/test-business/w/old-token', login)

    await waitFor(() => expect(setLang).toHaveBeenCalledWith('AR'))
  })

  it("falls back to the browser's language for an unknown link when nothing is saved", async () => {
    vi.spyOn(navigator, 'language', 'get').mockReturnValue('en-US')
    const login = vi.fn().mockRejectedValue({ response: { status: 400, data: { error: 'invalid', language: null } } })

    renderAt('/test-business/w/never-issued', login)

    await waitFor(() => expect(setLang).toHaveBeenCalledWith('EN'))
  })

  it("keeps the customer's saved language for an unknown link", async () => {
    localStorage.setItem('customerLang', 'HE')
    const login = vi.fn().mockRejectedValue({ response: { status: 400, data: { error: 'invalid', language: null } } })

    renderAt('/test-business/w/never-issued', login)

    await waitFor(() => expect(screen.getByText(/expired or is invalid/)).toBeInTheDocument())
    expect(setLang).not.toHaveBeenCalled()
  })
})
