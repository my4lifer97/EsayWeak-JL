import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import CancelOptionsModal from './CancelOptionsModal'
import { api } from '../../lib/api'

vi.mock('../../lib/api', () => ({
  api: { get: vi.fn(), post: vi.fn(), patch: vi.fn(), delete: vi.fn() },
}))

function renderModal(waitlistEnabled = true) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onClose = vi.fn()
  const onDone = vi.fn()
  render(
    <QueryClientProvider client={queryClient}>
      <CancelOptionsModal lang="EN" appointmentId="appt-1" waitlistEnabled={waitlistEnabled} onClose={onClose} onDone={onDone} />
    </QueryClientProvider>
  )
  return { onClose, onDone }
}

beforeEach(() => {
  vi.clearAllMocks()
})

describe('CancelOptionsModal', () => {
  it('shows all three options when the waitlist is enabled', () => {
    renderModal(true)

    expect(screen.getByText('Offer to Waitlist')).toBeInTheDocument()
    expect(screen.getByText('Cancel Without Notifying')).toBeInTheDocument()
    expect(screen.getByText('Replace Customer')).toBeInTheDocument()
  })

  it('hides "Offer to Waitlist" when the waitlist is not enabled for this business', () => {
    renderModal(false)

    expect(screen.queryByText('Offer to Waitlist')).not.toBeInTheDocument()
    expect(screen.getByText('Cancel Without Notifying')).toBeInTheDocument()
  })

  it('offering to the waitlist cancels with notifyWaitlist:true', async () => {
    vi.mocked(api.patch).mockResolvedValue({ data: { ok: true } })
    const { onDone } = renderModal(true)

    await userEvent.click(screen.getByText('Offer to Waitlist'))

    expect(api.patch).toHaveBeenCalledWith('/admin/appointments/appt-1', { status: 'CANCELLED', notifyWaitlist: true })
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('cancelling silently cancels with notifyWaitlist:false', async () => {
    vi.mocked(api.patch).mockResolvedValue({ data: { ok: true } })
    const { onDone } = renderModal(true)

    await userEvent.click(screen.getByText('Cancel Without Notifying'))

    expect(api.patch).toHaveBeenCalledWith('/admin/appointments/appt-1', { status: 'CANCELLED', notifyWaitlist: false })
    expect(onDone).toHaveBeenCalledTimes(1)
  })

  it('replacing the customer submits the new customer to the replace-customer endpoint', async () => {
    vi.mocked(api.patch).mockResolvedValue({ data: { ok: true } })
    const { onDone } = renderModal(true)

    await userEvent.click(screen.getByText('Replace Customer'))
    await userEvent.click(screen.getByText('New Customer'))
    await userEvent.type(screen.getByPlaceholderText('Customer Name'), 'Sam')
    await userEvent.type(screen.getByPlaceholderText('Phone Number'), '+15550001111')
    // The options-list "Replace Customer" button is gone once we're in the replace sub-view --
    // only the confirm button shares that label now, so this is unambiguous.
    await userEvent.click(screen.getByText('Replace Customer'))

    expect(api.patch).toHaveBeenCalledWith('/admin/appointments/appt-1/customer', {
      customerName: 'Sam', customerPhone: '+15550001111',
    })
    expect(onDone).toHaveBeenCalledTimes(1)
  })
})
