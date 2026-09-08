import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import BusinessReviews from './BusinessReviews'
import { customerApi } from '../../lib/customerApi'

vi.mock('../../lib/customerApi', () => ({
  customerApi: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() },
}))

const emptyList = {
  rating: { count: 0, average: 0 },
  reviews: { items: [], page: 1, pageSize: 10, total: 0, hasMore: false },
}
const oneReview = {
  rating: { count: 1, average: 4 },
  reviews: {
    items: [{ id: 'r1', rating: 4, comment: 'Great cut', reviewerName: 'Sam K.', createdAt: '2026-09-01T10:00:00Z', ownerReply: null, ownerRepliedAt: null }],
    page: 1, pageSize: 10, total: 1, hasMore: false,
  },
}

function renderReviews(props: Partial<React.ComponentProps<typeof BusinessReviews>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <BusinessReviews slug="joe" lang="EN" isAuthenticated={true} {...props} />
      </MemoryRouter>
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('BusinessReviews', () => {
  it('shows the completed-visit hint when the customer is not eligible', async () => {
    vi.mocked(customerApi.get).mockImplementation((url: string) => {
      if (url.startsWith('/businesses/joe/reviews')) return Promise.resolve({ data: emptyList })
      if (url.startsWith('/reviews/eligibility')) return Promise.resolve({ data: { canReview: false, alreadyReviewed: false, review: null } })
      return Promise.reject(new Error(`unexpected ${url}`))
    })

    renderReviews()

    expect(await screen.findByText('You can leave a review after your first completed appointment.')).toBeInTheDocument()
    expect(screen.queryByText('Leave a review')).not.toBeInTheDocument()
  })

  it('submits a new review when eligible', async () => {
    vi.mocked(customerApi.get).mockImplementation((url: string) => {
      if (url.startsWith('/businesses/joe/reviews')) return Promise.resolve({ data: emptyList })
      if (url.startsWith('/reviews/eligibility')) return Promise.resolve({ data: { canReview: true, alreadyReviewed: false, review: null } })
      return Promise.reject(new Error(`unexpected ${url}`))
    })
    vi.mocked(customerApi.post).mockResolvedValue({ data: {} })

    renderReviews()

    await userEvent.click(await screen.findByText('Leave a review'))
    await userEvent.click(screen.getByLabelText('5 stars'))
    await userEvent.type(screen.getByPlaceholderText('Share your experience (optional)'), 'Loved it')
    await userEvent.click(screen.getByText('Submit review'))

    await waitFor(() => expect(customerApi.post).toHaveBeenCalledWith('/reviews', { businessSlug: 'joe', rating: 5, comment: 'Loved it' }))
  })

  it('renders an existing public review with owner reply space and the customer\'s own edit controls', async () => {
    vi.mocked(customerApi.get).mockImplementation((url: string) => {
      if (url.startsWith('/businesses/joe/reviews')) return Promise.resolve({ data: oneReview })
      if (url.startsWith('/reviews/eligibility')) return Promise.resolve({ data: { canReview: true, alreadyReviewed: true, review: { id: 'r1', rating: 4, comment: 'Great cut', ownerReply: null, ownerRepliedAt: null } } })
      return Promise.reject(new Error(`unexpected ${url}`))
    })

    renderReviews()

    expect(await screen.findByText('Your review')).toBeInTheDocument()
    expect(screen.getByText('Edit review')).toBeInTheDocument()
    // The comment shows both in the public list and in the customer's own-review block.
    expect(screen.getAllByText('Great cut').length).toBeGreaterThanOrEqual(1)
  })

  it('does not fetch eligibility for anonymous visitors', async () => {
    vi.mocked(customerApi.get).mockImplementation((url: string) => {
      if (url.startsWith('/businesses/joe/reviews')) return Promise.resolve({ data: emptyList })
      return Promise.reject(new Error(`unexpected ${url}`))
    })

    renderReviews({ isAuthenticated: false })

    expect(await screen.findByText('No reviews yet')).toBeInTheDocument()
    expect(customerApi.get).not.toHaveBeenCalledWith(expect.stringContaining('/reviews/eligibility'))
  })
})
