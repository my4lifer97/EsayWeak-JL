import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import CustomerLoginPage from './CustomerLoginPage'
import { useCustomerAuth } from '../../lib/customerAuth'

vi.mock('../../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

function mockAuth(overrides: Partial<ReturnType<typeof useCustomerAuth>> = {}) {
  vi.mocked(useCustomerAuth).mockReturnValue({
    language: 'EN',
    requestOtp: vi.fn(),
    verifyOtp: vi.fn(),
    ...overrides,
  } as ReturnType<typeof useCustomerAuth>)
}

function renderPage(initialEntry = '/login') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <CustomerLoginPage />
    </MemoryRouter>
  )
}

describe('CustomerLoginPage', () => {
  it('requests a code for the entered phone and moves to the otp step', async () => {
    const requestOtp = vi.fn().mockResolvedValue({ isNewCustomer: true, devOtp: '123456' })
    mockAuth({ requestOtp })
    renderPage()

    await userEvent.type(screen.getByPlaceholderText('+1234567890'), '+15551234567')
    await userEvent.click(screen.getByRole('button', { name: 'Send Code' }))

    await waitFor(() => expect(requestOtp).toHaveBeenCalledWith('+15551234567'))
    expect(screen.getByText('123456')).toBeInTheDocument() // dev hint shows the code
  })

  it('shows the name fields only for a new customer, and verifies with them', async () => {
    const requestOtp = vi.fn().mockResolvedValue({ isNewCustomer: true, devOtp: '123456' })
    const verifyOtp = vi.fn().mockResolvedValue(undefined)
    mockAuth({ requestOtp, verifyOtp })
    renderPage()

    await userEvent.type(screen.getByPlaceholderText('+1234567890'), '+15551234567')
    await userEvent.click(screen.getByRole('button', { name: 'Send Code' }))
    await waitFor(() => screen.getByLabelText('First Name'))

    await userEvent.type(screen.getByLabelText('First Name'), 'Jane')
    await userEvent.type(screen.getByLabelText('Family Name'), 'Doe')
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }))

    await waitFor(() =>
      expect(verifyOtp).toHaveBeenCalledWith('+15551234567', '123456', 'Jane', 'Doe')
    )
  })

  it('shows an error message on an invalid code', async () => {
    const requestOtp = vi.fn().mockResolvedValue({ isNewCustomer: false })
    const verifyOtp = vi.fn().mockRejectedValue(new Error('bad code'))
    mockAuth({ requestOtp, verifyOtp })
    renderPage()

    await userEvent.type(screen.getByPlaceholderText('+1234567890'), '+15551234567')
    await userEvent.click(screen.getByRole('button', { name: 'Send Code' }))
    await waitFor(() => screen.getByPlaceholderText('123456'))
    await userEvent.type(screen.getByPlaceholderText('123456'), '000000')
    await userEvent.click(screen.getByRole('button', { name: 'Verify' }))

    await waitFor(() => expect(screen.getByText('Invalid or expired code')).toBeInTheDocument())
  })
})
