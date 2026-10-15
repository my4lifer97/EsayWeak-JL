import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider, Link } from 'react-router-dom'
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
  { id: 'svc-1', nameEn: 'Haircut', nameAr: 'قصة شعر', nameHe: 'תספורת', durationMinutes: 30, price: 50, isActive: true, isBookable: true, photoMode: 'None', galleryPhotos: [] },
]

function renderPage() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  // A data router, since the page uses useBlocker (unsaved-changes guard).
  const router = createMemoryRouter([
    { path: '/admin/services', element: <><Link to="/admin/dashboard">Dashboard</Link><ServicesPage /></> },
    { path: '/admin/dashboard', element: <div>Dashboard page</div> },
  ], { initialEntries: ['/admin/services'] })
  return render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  vi.mocked(useAuth).mockReturnValue({ language: 'EN' } as ReturnType<typeof useAuth>)
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

    await waitFor(() => expect(api.post).toHaveBeenCalledWith('/admin/items', expect.objectContaining({
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

    await waitFor(() => expect(api.patch).toHaveBeenCalledWith('/admin/items/svc-1', expect.objectContaining({ price: 60 })))
  })

  it('deletes a service after confirmation', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })
    vi.mocked(api.delete).mockResolvedValue({ data: {} })
    renderPage()
    await screen.findByText('Haircut')

    const confirmSpy = vi.spyOn(window, 'confirm')
    await userEvent.click(screen.getByText('Delete'))
    expect(api.delete).not.toHaveBeenCalled()
    await userEvent.click(screen.getByText('Yes, delete'))

    await waitFor(() => expect(api.delete).toHaveBeenCalledWith('/admin/items/svc-1'))
    expect(confirmSpy).not.toHaveBeenCalled()
  })

  it('does not delete when the confirmation is declined', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })
    renderPage()
    await screen.findByText('Haircut')

    await userEvent.click(screen.getByText('Delete'))
    await userEvent.click(screen.getByText('No, keep it'))

    expect(api.delete).not.toHaveBeenCalled()
  })

  it('asks before closing the form with unsaved edits', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })
    renderPage()
    await screen.findByText('Haircut')

    await userEvent.click(screen.getByText('Edit'))
    await userEvent.type(screen.getByLabelText('Name (English)'), ' deluxe')
    await userEvent.click(screen.getByLabelText('Close'))

    expect(screen.getByText('You have unsaved changes. Close without saving?')).toBeInTheDocument()
    await userEvent.click(screen.getByText('Keep editing'))
    expect((screen.getByLabelText('Name (English)') as HTMLInputElement).value).toBe('Haircut deluxe')

    await userEvent.click(screen.getByLabelText('Close'))
    await userEvent.click(screen.getByText('Discard changes'))
    expect(screen.queryByLabelText('Name (English)')).not.toBeInTheDocument()
  })

  it('closes an unchanged form without asking', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })
    renderPage()
    await screen.findByText('Haircut')

    await userEvent.click(screen.getByText('Edit'))
    await userEvent.click(screen.getByLabelText('Close'))

    expect(screen.queryByLabelText('Name (English)')).not.toBeInTheDocument()
    expect(screen.queryByText('You have unsaved changes. Close without saving?')).not.toBeInTheDocument()
  })

  it('asks before navigating away with unsaved edits', async () => {
    vi.mocked(api.get).mockResolvedValue({ data: services })
    renderPage()
    await screen.findByText('Haircut')

    await userEvent.click(screen.getByText('Edit'))
    await userEvent.type(screen.getByLabelText('Price'), '5')
    await userEvent.click(screen.getByText('Dashboard'))

    expect(await screen.findByText('You have unsaved changes. Leave without saving?')).toBeInTheDocument()
    await userEvent.click(screen.getByText('Keep editing'))
    expect(screen.queryByText('Dashboard page')).not.toBeInTheDocument()

    await userEvent.click(screen.getByText('Dashboard'))
    await userEvent.click(await screen.findByText('Leave without saving'))
    expect(await screen.findByText('Dashboard page')).toBeInTheDocument()
  })
})
