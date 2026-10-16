import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import CancelOptionsModal from './CancelOptionsModal'
import { api } from '../../lib/api'

vi.mock('../../lib/api', () => ({
  api: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() },
}))

function renderModal(waitlistEnabled = true, pendingCancellation = false, whatsAppNumber: string | null = '+972500000000') {
  vi.mocked(api.get).mockImplementation((url: string) => Promise.resolve({
    data: url === '/admin/settings' ? { whatsAppNumber } : [],
  }))
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  const onDone = vi.fn()
  render(
    <QueryClientProvider client={queryClient}>
      <CancelOptionsModal lang="EN" appointmentId="appt-1" waitlistEnabled={waitlistEnabled} pendingCancellation={pendingCancellation} onClose={onClose} onDone={onDone} />
    </QueryClientProvider>
  )
  return { onClose, onDone }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('CancelOptionsModal', () => {
  it('offers "Keep the Appointment" only for a cancellation request', () => {
    renderModal(true, false)
    expect(screen.queryByText('Keep the Appointment')).not.toBeInTheDocument()
  })

  it('shows the server error instead of failing silently', async () => {
    vi.mocked(api.patch).mockRejectedValue({ response: { data: { error: 'Slot is no longer available' } } })
    const { onDone } = renderModal(true)

    await userEvent.click(screen.getByText('Cancel Only'))

    expect(await screen.findByText('Slot is no longer available')).toBeInTheDocument()
    expect(onDone).not.toHaveBeenCalled()
  })

  it('keeping the appointment declines the request', async () => {
    vi.mocked(api.post).mockResolvedValue({ data: { id: 'appt-1' } })
    const { onDone } = renderModal(true, true)

    await userEvent.click(screen.getByText('Keep the Appointment'))

    expect(api.post).toHaveBeenCalledWith('/admin/appointments/appt-1/keep?notifyCustomer=true')
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('shows all three options when the waitlist is enabled', () => {
    renderModal(true)

    expect(screen.getByText('Offer to Waitlist')).toBeInTheDocument()
    expect(screen.getByText('Cancel Only')).toBeInTheDocument()
    expect(screen.getByText('Replace Customer')).toBeInTheDocument()
  })

  it('hides "Offer to Waitlist" when the waitlist is not enabled for this business', () => {
    renderModal(false)

    expect(screen.queryByText('Offer to Waitlist')).not.toBeInTheDocument()
    expect(screen.getByText('Cancel Only')).toBeInTheDocument()
  })

  it('offering to the waitlist cancels with notifyWaitlist:true', async () => {
    vi.mocked(api.patch).mockResolvedValue({ data: { ok: true } })
    const { onDone } = renderModal(true)

    await userEvent.click(screen.getByText('Offer to Waitlist'))

    expect(api.patch).toHaveBeenCalledWith('/admin/appointments/appt-1', { status: 'CANCELLED', notifyWaitlist: true, notifyCustomer: true })
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('cancelling only cancels with notifyWaitlist:false', async () => {
    vi.mocked(api.patch).mockResolvedValue({ data: { ok: true } })
    const { onDone } = renderModal(true)

    await userEvent.click(screen.getByText('Cancel Only'))

    expect(api.patch).toHaveBeenCalledWith('/admin/appointments/appt-1', { status: 'CANCELLED', notifyWaitlist: false, notifyCustomer: true })
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('replacing the customer submits the new customer to the replace-customer endpoint', async () => {
    vi.mocked(api.patch).mockResolvedValue({ data: { ok: true } })
    const { onDone } = renderModal(true)

    await userEvent.click(screen.getByText('Replace Customer'))
    await userEvent.click(screen.getByText('New Customer'))
    await userEvent.type(screen.getByPlaceholderText('Customer Name'), 'Sam')
    await userEvent.type(screen.getByPlaceholderText('Family Name'), 'Adams')
    await userEvent.type(screen.getByPlaceholderText('Phone Number'), '+15550001111')
    // The options-list "Replace Customer" button is gone once we're in the replace sub-view --
    // only the confirm button shares that label now, so this is unambiguous.
    await userEvent.click(screen.getByText('Replace Customer'))

    expect(api.patch).toHaveBeenCalledWith('/admin/appointments/appt-1/customer', {
      customerName: 'Sam', customerFamilyName: 'Adams', customerPhone: '+15550001111', notifyCustomer: true,
    })
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('unticking "let the customer know" sends notifyCustomer:false', async () => {
    vi.mocked(api.patch).mockResolvedValue({ data: { ok: true } })
    vi.mocked(api.post).mockResolvedValue({ data: { id: 'appt-1' } })
    renderModal(true, true)

    await userEvent.click(await screen.findByLabelText('Let the customer know on WhatsApp'))
    await userEvent.click(screen.getByText('Keep the Appointment'))
    await userEvent.click(screen.getByText('Cancel Only'))

    expect(api.post).toHaveBeenCalledWith('/admin/appointments/appt-1/keep?notifyCustomer=false')
    expect(api.patch).toHaveBeenCalledWith('/admin/appointments/appt-1', { status: 'CANCELLED', notifyWaitlist: false, notifyCustomer: false })
  })

  it('hides the WhatsApp checkbox when the business has no linked number', async () => {
    renderModal(true, false, null)

    await screen.findByText('Cancel Only')
    await new Promise((r) => setTimeout(r, 0))
    expect(screen.queryByLabelText('Let the customer know on WhatsApp')).not.toBeInTheDocument()
  })
})
