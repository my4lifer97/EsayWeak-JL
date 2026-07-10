import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Routes, Route } from 'react-router-dom'
import userEvent from '@testing-library/user-event'
import BookingWizard from './BookingWizard'
import { customerApi } from '../../lib/customerApi'
import { useCustomerAuth } from '../../lib/customerAuth'

vi.mock('../../lib/customerApi', () => ({
  customerApi: { get: vi.fn(), post: vi.fn() },
}))
vi.mock('../../lib/customerAuth', () => ({
  useCustomerAuth: vi.fn(),
}))

const barber = {
  slug: 'test-barber',
  name: 'Test Barber',
  language: 'EN',
  isRTL: false,
  activeDays: [0, 1, 2, 3, 4, 5, 6], // every day active — deterministic regardless of "today"
  services: [
    { id: 'svc-1', nameEn: 'Haircut', nameAr: 'قصة شعر', nameHe: 'תספורת', durationMinutes: 30, price: 50 },
  ],
}

function mockAnonymous() {
  vi.mocked(useCustomerAuth).mockReturnValue({ user: null, isAuthenticated: false, language: 'EN', setLang: vi.fn() } as ReturnType<typeof useCustomerAuth>)
}

function renderWizard() {
  return render(
    <MemoryRouter initialEntries={[`/${barber.slug}/book`]}>
      <Routes>
        <Route path="/:slug/book" element={<BookingWizard barber={barber} />} />
        <Route path="/:slug" element={<div>Barber main page</div>} />
      </Routes>
    </MemoryRouter>
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  mockAnonymous()
})

function findDateButtons() {
  // Step 2's date cells are the only buttons whose text contains a digit (the day number);
  // the "Haircut" service button and "← Back" nav button must both be excluded.
  return screen.getAllByRole('button').filter((b) => /\d/.test(b.textContent ?? '') && !b.textContent?.includes('Haircut'))
}

async function advanceToStep4() {
  renderWizard()

  await userEvent.click(screen.getByText('Haircut'))

  vi.mocked(customerApi.get).mockResolvedValue({ data: { slots: [{ start: '09:00', end: '09:30' }] } })
  await userEvent.click(findDateButtons()[0])

  await waitFor(() => expect(screen.getByText('09:00')).toBeInTheDocument())
  await userEvent.click(screen.getByText('09:00'))

  await waitFor(() => expect(screen.getByText('Your Details')).toBeInTheDocument())
}

describe('BookingWizard', () => {
  it('step 1 lists services and selecting one advances to date selection', async () => {
    renderWizard()

    expect(screen.getByText('Select a Service')).toBeInTheDocument()
    expect(screen.getByText('Haircut')).toBeInTheDocument()

    await userEvent.click(screen.getByText('Haircut'))

    expect(screen.getByText('Select a Date')).toBeInTheDocument()
  })

  it('back button returns to the previous step', async () => {
    renderWizard()
    await userEvent.click(screen.getByText('Haircut'))
    expect(screen.getByText('Select a Date')).toBeInTheDocument()

    await userEvent.click(screen.getByText('← Back'))

    expect(screen.getByText('Select a Service')).toBeInTheDocument()
  })

  it('picking a date fetches availability and advances to time selection', async () => {
    renderWizard()
    await userEvent.click(screen.getByText('Haircut'))

    vi.mocked(customerApi.get).mockResolvedValue({ data: { slots: [{ start: '09:00', end: '09:30' }] } })
    const dateButtons = findDateButtons()
    await userEvent.click(dateButtons[0])

    expect(screen.getByText('Select a Time')).toBeInTheDocument()
    await waitFor(() => expect(customerApi.get).toHaveBeenCalledWith(expect.stringContaining('/test-barber/availability?date=')))
    await waitFor(() => expect(screen.getByText('09:00')).toBeInTheDocument())
  })

  it('shows "no times" when availability returns no slots', async () => {
    renderWizard()
    await userEvent.click(screen.getByText('Haircut'))
    vi.mocked(customerApi.get).mockResolvedValue({ data: { slots: [] } })
    const dateButtons = findDateButtons()

    await userEvent.click(dateButtons[0])

    await waitFor(() => expect(screen.getByText('No available times. Please pick another day.')).toBeInTheDocument())
  })

  it('submits the booking with the entered details and redirects to the barber\'s main page', async () => {
    await advanceToStep4()
    vi.mocked(customerApi.post).mockResolvedValue({ data: { appointmentId: 'appt-1', cancelToken: 'tok-1' } })

    await userEvent.type(screen.getByLabelText('First Name'), 'Jane')
    await userEvent.type(screen.getByLabelText('Phone Number'), '+15551234567')
    await userEvent.click(screen.getByText('Confirm Appointment'))

    await waitFor(() => expect(screen.getByText('Barber main page')).toBeInTheDocument())
    expect(customerApi.post).toHaveBeenCalledWith('/test-barber/appointments', expect.objectContaining({
      serviceId: 'svc-1',
      startTime: '09:00',
      customerName: 'Jane',
      customerPhone: '+15551234567',
    }))
  })

  it('shows the server error message and stays on the details step when booking fails', async () => {
    await advanceToStep4()
    vi.mocked(customerApi.post).mockRejectedValue({ response: { data: { error: 'Slot no longer available' } } })

    await userEvent.type(screen.getByLabelText('First Name'), 'Jane')
    await userEvent.type(screen.getByLabelText('Phone Number'), '+15551234567')
    await userEvent.click(screen.getByText('Confirm Appointment'))

    await waitFor(() => expect(screen.getByText('Slot no longer available')).toBeInTheDocument())
    expect(screen.getByText('Your Details')).toBeInTheDocument()
  })

  it('prefills and locks the phone field when the customer is authenticated', async () => {
    vi.mocked(useCustomerAuth).mockReturnValue({
      user: { id: '1', name: 'Jane', familyName: 'Doe', phone: '+15559998888' },
      isAuthenticated: true, language: 'EN', setLang: vi.fn(),
    } as ReturnType<typeof useCustomerAuth>)

    renderWizard()
    await userEvent.click(screen.getByText('Haircut'))
    vi.mocked(customerApi.get).mockResolvedValue({ data: { slots: [{ start: '09:00', end: '09:30' }] } })
    const dateButtons = findDateButtons()
    await userEvent.click(dateButtons[0])
    await waitFor(() => screen.getByText('09:00'))
    await userEvent.click(screen.getByText('09:00'))

    const phoneInput = await screen.findByLabelText('Phone Number') as HTMLInputElement
    expect(phoneInput.value).toBe('+15559998888')
    expect(phoneInput).toBeDisabled()
    expect((screen.getByLabelText('First Name') as HTMLInputElement).value).toBe('Jane Doe')
  })
})
