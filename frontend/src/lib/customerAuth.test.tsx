import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerAuthProvider, useCustomerAuth } from './customerAuth'
import { customerApi } from './customerApi'

vi.mock('./customerApi', () => ({
  customerApi: { post: vi.fn() },
}))

function TestConsumer() {
  const { user, requestOtp, verifyOtp, logout, isAuthenticated } = useCustomerAuth()
  return (
    <div>
      <div data-testid="authed">{String(isAuthenticated)}</div>
      <div data-testid="user">{user ? user.phone : 'none'}</div>
      <button onClick={() => requestOtp('+15550001111')}>request</button>
      <button onClick={() => verifyOtp('+15550001111', '123456', 'First', 'Last')}>verify</button>
      <button onClick={logout}>logout</button>
    </div>
  )
}

function renderWithProvider() {
  return render(
    <CustomerAuthProvider>
      <TestConsumer />
    </CustomerAuthProvider>
  )
}

beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks()
})

describe('CustomerAuthProvider', () => {
  it('starts unauthenticated with no stored user', () => {
    renderWithProvider()
    expect(screen.getByTestId('authed').textContent).toBe('false')
  })

  it('hydrates from localStorage on mount', () => {
    localStorage.setItem('customerUser', JSON.stringify({ id: '1', name: 'First', familyName: 'Last', phone: '+15550001111' }))
    renderWithProvider()
    expect(screen.getByTestId('authed').textContent).toBe('true')
    expect(screen.getByTestId('user').textContent).toBe('+15550001111')
  })

  it('requestOtp calls the OTP endpoint and returns the response data without touching storage', async () => {
    vi.mocked(customerApi.post).mockResolvedValue({ data: { isNewCustomer: true, devOtp: '999999' } })
    renderWithProvider()

    await userEvent.click(screen.getByText('request'))

    expect(customerApi.post).toHaveBeenCalledWith('/customer/auth/otp', { phone: '+15550001111' })
    expect(localStorage.getItem('customerToken')).toBeNull()
  })

  it('verifyOtp stores token/user and updates context', async () => {
    vi.mocked(customerApi.post).mockResolvedValue({
      data: { token: 't1', customerId: '1', name: 'First', familyName: 'Last', phone: '+15550001111' },
    })
    renderWithProvider()

    await userEvent.click(screen.getByText('verify'))

    await waitFor(() => expect(screen.getByTestId('authed').textContent).toBe('true'))
    expect(localStorage.getItem('customerToken')).toBe('t1')
    expect(JSON.parse(localStorage.getItem('customerUser')!).phone).toBe('+15550001111')
  })

  it('logout clears storage and context', async () => {
    localStorage.setItem('customerUser', JSON.stringify({ id: '1', name: 'First', familyName: 'Last', phone: '+15550001111' }))
    localStorage.setItem('customerToken', 't1')
    renderWithProvider()

    await userEvent.click(screen.getByText('logout'))

    expect(screen.getByTestId('authed').textContent).toBe('false')
    expect(localStorage.getItem('customerToken')).toBeNull()
  })
})
