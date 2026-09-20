import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import BusinessPage from './BusinessPage'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'

vi.mock('../../lib/customerApi', () => ({
  customerApi: { get: vi.fn(), post: vi.fn(), delete: vi.fn() },
}))
vi.mock('../../lib/customerAuth', () => ({ useCustomerAuth: vi.fn() }))
vi.mock('../../components/customer/BusinessReviews', () => ({ default: () => <div>Reviews Section</div> }))
vi.mock('../../components/customer/LanguageSwitcher', () => ({ default: () => null }))

const info = {
  slug: 'joe', name: 'Joe the Barber', description: 'Best fades', logo: null,
  language: 'EN', isRTL: false, activeDays: [1, 2], items: [], isFollowed: false,
  city: 'Haifa', addressLine: '1 Main St', mapUrl: 'https://maps.example/joe',
  ratingCount: 12, ratingAverage: 4.6,
}

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/joe']}>
        <Routes>
          <Route path="/:slug" element={<BusinessPage />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(customerApi.get).mockImplementation((url: string) => {
    if (url === '/joe/info') return Promise.resolve({ data: info })
    if (url.startsWith('/customer/appointments')) return Promise.resolve({ data: [] })
    return Promise.reject(new Error(`unexpected url ${url}`))
  })
})

describe('BusinessPage (public storefront)', () => {
  it('renders for an anonymous visitor with rating and location', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderPage()

    expect(await screen.findByText('Joe the Barber')).toBeInTheDocument()
    expect(screen.getByText(/1 Main St, Haifa/)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'View on map' })).toHaveAttribute('href', 'https://maps.example/joe')
    expect(screen.getByText('Reviews Section')).toBeInTheDocument()
  })

  it('disables the follow button for anonymous visitors', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderPage()

    expect(await screen.findByRole('button', { name: 'Follow' })).toBeDisabled()
  })

  it('enables the follow button when authenticated', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: true, language: 'EN' } as ReturnType<typeof useCustomerAuth>)

    renderPage()

    expect(await screen.findByRole('button', { name: 'Follow' })).toBeEnabled()
  })

  it('lists bookable services with their duration and price', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ isAuthenticated: false, language: 'EN' } as ReturnType<typeof useCustomerAuth>)
    vi.mocked(customerApi.get).mockImplementation((url: string) => {
      if (url === '/joe/info') return Promise.resolve({
        data: {
          ...info,
          items: [
            { id: '1', nameEn: 'Haircut', nameAr: 'Haircut', nameHe: 'Haircut', durationMinutes: 30, price: 50, isBookable: true },
            { id: '2', nameEn: 'Consultation', nameAr: 'Consultation', nameHe: 'Consultation', durationMinutes: null, price: null, isBookable: false },
          ],
        },
      })
      if (url.startsWith('/customer/appointments')) return Promise.resolve({ data: [] })
      return Promise.reject(new Error(`unexpected url ${url}`))
    })

    renderPage()

    expect(await screen.findByText('Haircut')).toBeInTheDocument()
    expect(screen.getByText('30 min')).toBeInTheDocument()
    expect(screen.getByText('₪50')).toBeInTheDocument()
    expect(screen.getByText('Consultation')).toBeInTheDocument()
  })
})
