import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import BrowseBarbersPage from './BrowseBarbersPage'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'

vi.mock('../../lib/customerApi', () => ({
  customerApi: { get: vi.fn(), post: vi.fn(), delete: vi.fn() },
}))
vi.mock('../../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

const searchResults = [
  { slug: 'joe', name: 'Joe the Barber', description: null, logo: null, isFollowed: false },
]
const followedList = [
  { slug: 'mo', name: 'Mo the Barber', description: null, logo: null, isFollowed: true },
]

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <BrowseBarbersPage />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(useCustomerAuth).mockReturnValue({ language: 'EN', isAuthenticated: true } as ReturnType<typeof useCustomerAuth>)
  vi.mocked(customerApi.get).mockImplementation((url: string) => {
    if (url.startsWith('/barbers/search')) return Promise.resolve({ data: searchResults })
    if (url === '/barbers/followed') return Promise.resolve({ data: followedList })
    return Promise.reject(new Error(`unexpected url ${url}`))
  })
})

describe('BrowseBarbersPage', () => {
  it('shows the followed barbers list', async () => {
    renderPage()

    expect(await screen.findByText('Mo the Barber')).toBeInTheDocument()
    expect(screen.getByText('Barbers You Follow')).toBeInTheDocument()
  })

  it('shows an empty state when there are no follows', async () => {
    vi.mocked(customerApi.get).mockImplementation((url: string) => {
      if (url.startsWith('/barbers/search')) return Promise.resolve({ data: searchResults })
      if (url === '/barbers/followed') return Promise.resolve({ data: [] })
      return Promise.reject(new Error(`unexpected url ${url}`))
    })

    renderPage()

    expect(await screen.findByText("You're not following any barbers yet.")).toBeInTheDocument()
  })

  it('does not fetch followed barbers when not authenticated', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ language: 'EN', isAuthenticated: false } as ReturnType<typeof useCustomerAuth>)

    renderPage()

    await screen.findByText("You're not following any barbers yet.")
    expect(customerApi.get).not.toHaveBeenCalledWith('/barbers/followed')
  })

  it('does not show search results until the customer types a query', async () => {
    renderPage()

    await screen.findByText('Mo the Barber')
    expect(screen.queryByText('Joe the Barber')).not.toBeInTheDocument()
    expect(customerApi.get).not.toHaveBeenCalledWith(expect.stringContaining('/barbers/search'))
  })

  it('shows search results once a query is typed', async () => {
    renderPage()

    await userEvent.type(screen.getByPlaceholderText('Search barbers...'), 'Joe')

    expect(await screen.findByText('Joe the Barber')).toBeInTheDocument()
  })

  it('removing a followed barber calls unfollow and refreshes the list', async () => {
    vi.mocked(customerApi.delete).mockResolvedValue({ data: { ok: true } })
    renderPage()
    await screen.findByText('Mo the Barber')

    await userEvent.click(screen.getByText('Remove'))

    await waitFor(() => expect(customerApi.delete).toHaveBeenCalledWith('/barbers/mo/follow'))
  })

  it('following a barber from search results calls the follow endpoint', async () => {
    vi.mocked(customerApi.post).mockResolvedValue({ data: { ok: true } })
    renderPage()

    await userEvent.type(screen.getByPlaceholderText('Search barbers...'), 'Joe')
    await screen.findByText('Joe the Barber')

    await userEvent.click(screen.getByText('Follow'))

    await waitFor(() => expect(customerApi.post).toHaveBeenCalledWith('/barbers/joe/follow'))
  })
})
