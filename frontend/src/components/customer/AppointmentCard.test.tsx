import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import userEvent from '@testing-library/user-event'
import AppointmentCard, { type Appointment } from './AppointmentCard'
import { customerApi } from '../../lib/customerApi'

vi.mock('../../lib/customerApi', () => ({
  customerApi: { get: vi.fn(), post: vi.fn(), patch: vi.fn() },
}))

const appt: Appointment = {
  id: 'appt-1', businessSlug: 'shop', businessName: 'Shop',
  date: '2030-01-01', startTime: '09:00', endTime: '09:30', notes: null, status: 'CONFIRMED', cancelToken: 't',
  item: { id: 'svc-1', nameEn: 'Haircut', nameAr: '', nameHe: '', durationMinutes: 30, price: 50 }, photoUrl: null,
}

function renderCard(onChanged = vi.fn()) {
  render(
    <QueryClientProvider client={new QueryClient()}>
      <MemoryRouter>
        <AppointmentCard appt={appt} lang="EN" onChanged={onChanged} />
      </MemoryRouter>
    </QueryClientProvider>
  )
  return onChanged
}

beforeEach(() => vi.clearAllMocks())

describe('AppointmentCard cancel', () => {
  // Native confirm() is silently blocked in WhatsApp's in-app browser, so cancelling must
  // never depend on it.
  it('asks on the page, not with window.confirm, and cancels on "Yes"', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm')
    vi.mocked(customerApi.post).mockResolvedValue({ data: { ok: true } })
    const onChanged = renderCard()

    await userEvent.click(screen.getByText('Cancel Appointment'))
    expect(customerApi.post).not.toHaveBeenCalled()
    await userEvent.click(screen.getByText('Yes, cancel it'))

    await waitFor(() => expect(customerApi.post).toHaveBeenCalledWith('/customer/appointments/appt-1/cancel'))
    expect(onChanged).toHaveBeenCalled()
    expect(confirmSpy).not.toHaveBeenCalled()
  })

  it('"No" keeps the appointment', async () => {
    renderCard()
    await userEvent.click(screen.getByText('Cancel Appointment'))
    await userEvent.click(screen.getByText('No, keep it'))

    expect(screen.queryByText('Yes, cancel it')).not.toBeInTheDocument()
    expect(customerApi.post).not.toHaveBeenCalled()
  })

  it('shows the server error instead of failing silently', async () => {
    vi.mocked(customerApi.post).mockRejectedValue({ response: { data: { error: 'This appointment can no longer be modified' } } })
    renderCard()

    await userEvent.click(screen.getByText('Cancel Appointment'))
    await userEvent.click(screen.getByText('Yes, cancel it'))

    await waitFor(() => expect(screen.getByText('This appointment can no longer be modified')).toBeInTheDocument())
  })
})
