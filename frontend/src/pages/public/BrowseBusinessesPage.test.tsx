import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import BrowseBusinessesPage from './BrowseBusinessesPage'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'

vi.mock('../../lib/customerApi', () => ({
  customerApi: { get: vi.fn(), post: vi.fn(), delete: vi.fn() },
}))
vi.mock('../../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

const card = (over: Record<string, unknown> = {}) => ({
  slug: 'joe', name: 'Joe the Business', description: null, logo: null, isFollowed: false,
  businessTypeKey: 'barber', city: 'Haifa', ratingAverage: 4.5, ratingCount: 10, followerCount: 3,
  ...over,
})
const pageOf = (items: unknown[], over: Record<string, unknown> = {}) => ({
  items, page: 1, pageSize: 12, total: items.length, hasMore: false, ...over,
})

const businessTypes = [
  { id: 't1', key: 'barber', displayNameEn: 'Barber Shop', displayNameAr: 'حلاق', displayNameHe: 'ספר' },
  { id: 't2', key: 'salon', displayNameEn: 'Beauty Salon', displayNameAr: 'صالون', displayNameHe: 'מספרה' },
]
const cities = ['Haifa', 'Tel Aviv']
const followedList = [card({ slug: 'mo', name: 'Mo the Business', isFollowed: true })]

let searchImpl: () => Promise<{ data: unknown }>

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <BrowseBusinessesPage />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(useCustomerAuth).mockReturnValue({ language: 'EN', isAuthenticated: true } as ReturnType<typeof useCustomerAuth>)
  searchImpl = () => Promise.resolve({ data: pageOf([card()]) })
  vi.mocked(customerApi.get).mockImplementation((url: string) => {
    if (url === '/businesses/search') return searchImpl()
    if (url === '/business-types') return Promise.resolve({ data: businessTypes })
    if (url === '/businesses/cities') return Promise.resolve({ data: cities })
    if (url === '/businesses/followed') return Promise.resolve({ data: followedList })
    return Promise.reject(new Error(`unexpected url ${url}`))
  })
})

describe('BrowseBusinessesPage', () => {
  it('shows a ranked list by default with no query typed', async () => {
    renderPage()

    expect(await screen.findByText('Joe the Business')).toBeInTheDocument()
    // card metadata: follower count (city "Haifa" also appears as a <option> in the city select)
    expect(screen.getByText(/3 followers/)).toBeInTheDocument()
    expect(customerApi.get).toHaveBeenCalledWith('/businesses/search', expect.objectContaining({
      params: expect.objectContaining({ sort: 'rating', page: 1 }),
    }))
  })

  it('passes the typed query to the search endpoint', async () => {
    renderPage()
    await screen.findByText('Joe the Business')

    await userEvent.type(screen.getByPlaceholderText('Search businesses...'), 'Joe')

    await waitFor(() => expect(customerApi.get).toHaveBeenCalledWith('/businesses/search', expect.objectContaining({
      params: expect.objectContaining({ query: 'Joe' }),
    })))
  })

  it('filters by category chip', async () => {
    renderPage()
    await screen.findByText('Beauty Salon') // chip rendered from /business-types

    await userEvent.click(screen.getByRole('button', { name: 'Beauty Salon' }))

    await waitFor(() => expect(customerApi.get).toHaveBeenCalledWith('/businesses/search', expect.objectContaining({
      params: expect.objectContaining({ businessTypeKey: 'salon' }),
    })))
  })

  it('changes the sort order', async () => {
    renderPage()
    await screen.findByText('Joe the Business')

    await userEvent.selectOptions(screen.getByDisplayValue('Top rated'), 'newest')

    await waitFor(() => expect(customerApi.get).toHaveBeenCalledWith('/businesses/search', expect.objectContaining({
      params: expect.objectContaining({ sort: 'newest' }),
    })))
  })

  it('loads more pages and appends the results', async () => {
    let call = 0
    searchImpl = () => {
      call += 1
      return call === 1
        ? Promise.resolve({ data: pageOf([card({ slug: 'joe', name: 'Joe the Business' })], { hasMore: true, total: 2 }) })
        : Promise.resolve({ data: pageOf([card({ slug: 'ann', name: 'Ann the Business' })], { page: 2, hasMore: false, total: 2 }) })
    }
    renderPage()
    await screen.findByText('Joe the Business')

    await userEvent.click(screen.getByRole('button', { name: 'Load more' }))

    expect(await screen.findByText('Ann the Business')).toBeInTheDocument()
    expect(screen.getByText('Joe the Business')).toBeInTheDocument()
  })

  it('shows an empty state when nothing matches', async () => {
    searchImpl = () => Promise.resolve({ data: pageOf([]) })
    renderPage()

    expect(await screen.findByText('No businesses match your filters.')).toBeInTheDocument()
  })

  it('shows the followed businesses list', async () => {
    renderPage()

    expect(await screen.findByText('Mo the Business')).toBeInTheDocument()
    expect(screen.getByText('Businesses You Follow')).toBeInTheDocument()
  })

  it('hides the followed section entirely when not authenticated', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ language: 'EN', isAuthenticated: false } as ReturnType<typeof useCustomerAuth>)

    renderPage()

    await screen.findByText('Joe the Business')
    expect(screen.queryByText('Businesses You Follow')).not.toBeInTheDocument()
    expect(customerApi.get).not.toHaveBeenCalledWith('/businesses/followed')
  })

  it('disables the follow button for logged-out visitors', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ language: 'EN', isAuthenticated: false } as ReturnType<typeof useCustomerAuth>)
    renderPage()
    await screen.findByText('Joe the Business')

    expect(screen.getByRole('button', { name: 'Follow' })).toBeDisabled()
  })

  it('following a business from the results calls the follow endpoint', async () => {
    vi.mocked(customerApi.post).mockResolvedValue({ data: { ok: true } })
    renderPage()
    await screen.findByText('Joe the Business')

    await userEvent.click(screen.getByRole('button', { name: 'Follow' }))

    await waitFor(() => expect(customerApi.post).toHaveBeenCalledWith('/businesses/joe/follow'))
  })

  it('removing a followed business calls unfollow', async () => {
    vi.mocked(customerApi.delete).mockResolvedValue({ data: { ok: true } })
    renderPage()
    await screen.findByText('Mo the Business')

    await userEvent.click(screen.getByText('Remove'))

    await waitFor(() => expect(customerApi.delete).toHaveBeenCalledWith('/businesses/mo/follow'))
  })
})
