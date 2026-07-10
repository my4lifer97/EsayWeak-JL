import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import ServicesPage from './ServicesPage'
import { api } from '../../lib/api'
import { useAuth } from '../../lib/auth'

vi.mock('../../lib/api', () => ({
  api: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() },
}))
vi.mock('../../lib/auth', () => ({
  useAuth: vi.fn(),
}))

const services = [
  { id: 'svc-1', nameEn: 'Haircut', nameAr: 'قصة شعر', nameHe: 'תספורת', durationMinutes: 30, price: 50, isActive: true },
]

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <ServicesPage />
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(useAuth).mockReturnValue({ language: 'EN' } as ReturnType<typeof useAuth>)
  vi.stubGlobal('confirm', vi.fn(() => true))
})

describe('ServicesPage', () => {
  it('renders the fetched services', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })

    renderPage()

    expect(await screen.findByText('Haircut')).toBeInTheDocument()
    expect(screen.getByText('30 min · ₪50')).toBeInTheDocument()
  })

  it('shows an empty state when there are no services', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [] })

    renderPage()

    expect(await screen.findByText('No services yet.')).toBeInTheDocument()
  })

  it('creates a new service via the form', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: [] })
    vi.mocked(api.post).mockResolvedValue({ data: {} })
    renderPage()
    await screen.findByText('No services yet.')

    await userEvent.click(screen.getByText('+ Add Service'))
    await userEvent.type(screen.getByLabelText('Name (English)'), 'Shave')
    await userEvent.type(screen.getByLabelText('Name (Arabic)'), 'حلاقة')
    await userEvent.type(screen.getByLabelText('Name (Hebrew)'), 'גילוח')
    await userEvent.type(screen.getByLabelText('Price'), '25')
    await userEvent.click(screen.getByText('Save Service'))

    await waitFor(() => expect(api.post).toHaveBeenCalledWith('/admin/services', expect.objectContaining({
      nameEn: 'Shave', nameAr: 'حلاقة', nameHe: 'גילוח', durationMinutes: 30, price: 25,
    })))
  })

  it('opens the edit form prefilled and submits a patch', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })
    vi.mocked(api.patch).mockResolvedValue({ data: {} })
    renderPage()
    await screen.findByText('Haircut')

    await userEvent.click(screen.getByText('Edit'))
    expect((screen.getByLabelText('Name (English)') as HTMLInputElement).value).toBe('Haircut')

    await userEvent.clear(screen.getByLabelText('Price'))
    await userEvent.type(screen.getByLabelText('Price'), '60')
    await userEvent.click(screen.getByText('Save Service'))

    await waitFor(() => expect(api.patch).toHaveBeenCalledWith('/admin/services/svc-1', expect.objectContaining({ price: 60 })))
  })

  it('deletes a service after confirmation', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })
    vi.mocked(api.delete).mockResolvedValue({ data: {} })
    renderPage()
    await screen.findByText('Haircut')

    await userEvent.click(screen.getByText('Delete'))

    expect(confirm).toHaveBeenCalled()
    await waitFor(() => expect(api.delete).toHaveBeenCalledWith('/admin/services/svc-1'))
  })

  it('does not delete when the confirmation is declined', async () => {
    vi.stubGlobal('confirm', vi.fn(() => false))
    vi.mocked(api.get).mockResolvedValue({ data: services })
    renderPage()
    await screen.findByText('Haircut')

    await userEvent.click(screen.getByText('Delete'))

    expect(api.delete).not.toHaveBeenCalled()
  })
})
