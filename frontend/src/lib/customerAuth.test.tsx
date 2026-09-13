import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { CustomerAuthProvider, useCustomerAuth } from './customerAuth'
import { customerApi } from './customerApi'

vi.mock('./customerApi', () => ({
  customerApi: { post: vi.fn() },
}))

function TestConsumer() {
  const { user, loginWithWhatsAppToken, requestOtp, verifyOtp, logout, isAuthenticated, language } = useCustomerAuth()
  return (
    <div>
      <div data-testid="authed">{String(isAuthenticated)}</div>
      <div data-testid="user">{user ? user.phone : 'none'}</div>
      <div data-testid="language">{language}</div>
      <button onClick={() => loginWithWhatsAppToken('wa-token-1')}>login</button>
      <button onClick={() => requestOtp('+15550009999')}>request-otp</button>
      <button onClick={() => verifyOtp('+15550009999', '123456', 'First', 'Last')}>verify-otp</button>
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

  it('loginWithWhatsAppToken redeems the token, stores the session, and returns the redirect target', async () => {
    vi.mocked(customerApi.post).mockResolvedValue({
      data: { token: 't1', customerId: '1', name: 'First', familyName: 'Last', phone: '+15550001111', businessSlug: 'test-business', itemId: 'svc-1' },
    })
    renderWithProvider()

    await userEvent.click(screen.getByText('login'))

    expect(customerApi.post).toHaveBeenCalledWith('/customer/auth/whatsapp', { token: 'wa-token-1' })
    await waitFor(() => expect(screen.getByTestId('authed').textContent).toBe('true'))
    expect(localStorage.getItem('customerToken')).toBe('t1')
    expect(JSON.parse(localStorage.getItem('customerUser')!).phone).toBe('+15550001111')
  })

  it('loginWithWhatsAppToken adopts the language detected server-side from the WhatsApp conversation', async () => {
    localStorage.setItem('customerLang', 'HE') // whatever was last stored in this browser
    vi.mocked(customerApi.post).mockResolvedValue({
      data: { token: 't1', customerId: '1', name: 'First', familyName: 'Last', phone: '+15550001111', businessSlug: 'test-business', itemId: 'svc-1', language: 'AR' },
    })
    renderWithProvider()

    await userEvent.click(screen.getByText('login'))

    await waitFor(() => expect(screen.getByTestId('language').textContent).toBe('AR'))
    expect(localStorage.getItem('customerLang')).toBe('AR')
  })

  it('requestOtp posts the phone to the otp endpoint', async () => {
    vi.mocked(customerApi.post).mockResolvedValue({ data: { isNewCustomer: true, devOtp: '654321' } })
    renderWithProvider()

    await userEvent.click(screen.getByText('request-otp'))

    await waitFor(() =>
      expect(customerApi.post).toHaveBeenCalledWith('/customer/auth/otp', { phone: '+15550009999' })
    )
  })

  it('verifyOtp posts phone/otp/name/familyName and stores the session, same as WhatsApp login', async () => {
    vi.mocked(customerApi.post).mockResolvedValue({
      data: { token: 't2', customerId: '2', name: 'First', familyName: 'Last', phone: '+15550009999' },
    })
    renderWithProvider()

    await userEvent.click(screen.getByText('verify-otp'))

    expect(customerApi.post).toHaveBeenCalledWith('/customer/auth/verify', {
      phone: '+15550009999', otp: '123456', name: 'First', familyName: 'Last',
    })
    await waitFor(() => expect(screen.getByTestId('authed').textContent).toBe('true'))
    expect(localStorage.getItem('customerToken')).toBe('t2')
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
