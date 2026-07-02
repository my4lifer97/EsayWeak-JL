import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AuthProvider, useAuth } from './auth'
import { api } from './api'

vi.mock('./api', () => ({
  api: { post: vi.fn() },
}))

function TestConsumer() {
  const { user, login, logout, isAuthenticated, language, setLang } = useAuth()
  return (
    <div>
      <div data-testid="authed">{String(isAuthenticated)}</div>
      <div data-testid="user">{user ? user.email : 'none'}</div>
      <div data-testid="lang">{language}</div>
      <button onClick={() => login('a@example.com', 'pw')}>login</button>
      <button onClick={logout}>logout</button>
      <button onClick={() => setLang('AR')}>setlang</button>
    </div>
  )
}

function renderWithProvider() {
  return render(
    <AuthProvider>
      <TestConsumer />
    </AuthProvider>
  )
}

beforeEach(() => {
  localStorage.clear()
  vi.clearAllMocks()
})

describe('AuthProvider', () => {
  it('starts unauthenticated with no stored user', () => {
    renderWithProvider()
    expect(screen.getByTestId('authed').textContent).toBe('false')
  })

  it('hydrates from localStorage on mount', () => {
    localStorage.setItem('user', JSON.stringify({ id: '1', name: 'A', email: 'a@example.com', slug: 'a' }))
    renderWithProvider()
    expect(screen.getByTestId('authed').textContent).toBe('true')
    expect(screen.getByTestId('user').textContent).toBe('a@example.com')
  })

  it('login stores token/user and updates context', async () => {
    vi.mocked(api.post).mockResolvedValue({ data: { token: 't1', id: '1', name: 'A', email: 'a@example.com', slug: 'a' } })
    renderWithProvider()

    await userEvent.click(screen.getByText('login'))

    await waitFor(() => expect(screen.getByTestId('authed').textContent).toBe('true'))
    expect(localStorage.getItem('token')).toBe('t1')
    expect(JSON.parse(localStorage.getItem('user')!).email).toBe('a@example.com')
  })

  it('logout clears storage and context', async () => {
    localStorage.setItem('user', JSON.stringify({ id: '1', name: 'A', email: 'a@example.com', slug: 'a' }))
    localStorage.setItem('token', 't1')
    renderWithProvider()

    await userEvent.click(screen.getByText('logout'))

    expect(screen.getByTestId('authed').textContent).toBe('false')
    expect(localStorage.getItem('token')).toBeNull()
  })

  it('setLang persists the language choice', async () => {
    renderWithProvider()

    await userEvent.click(screen.getByText('setlang'))

    expect(screen.getByTestId('lang').textContent).toBe('AR')
    expect(localStorage.getItem('lang')).toBe('AR')
  })
})
