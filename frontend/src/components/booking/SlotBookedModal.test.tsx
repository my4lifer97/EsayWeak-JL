import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import SlotBookedModal from './SlotBookedModal'

function renderModal(overrides: Partial<React.ComponentProps<typeof SlotBookedModal>> = {}) {
  const onJoinWaitlist = vi.fn()
  const onClose = vi.fn()
  render(
    <SlotBookedModal
      lang="EN"
      dir="ltr"
      waitlistEnabled={true}
      joining={false}
      joined={false}
      onJoinWaitlist={onJoinWaitlist}
      onClose={onClose}
      {...overrides}
    />
  )
  return { onJoinWaitlist, onClose }
}

describe('SlotBookedModal', () => {
  it('shows the already-booked message and both actions when waitlist is enabled', () => {
    renderModal()

    expect(screen.getByText('Already Booked')).toBeInTheDocument()
    expect(screen.getByText('Join Waitlist')).toBeEnabled()
    expect(screen.getByText('Choose Another Appointment')).toBeInTheDocument()
  })

  it('calls onJoinWaitlist when the join button is clicked', async () => {
    const { onJoinWaitlist } = renderModal()

    await userEvent.click(screen.getByText('Join Waitlist'))

    expect(onJoinWaitlist).toHaveBeenCalledTimes(1)
  })

  it('calls onClose when choosing another appointment', async () => {
    const { onClose } = renderModal()

    await userEvent.click(screen.getByText('Choose Another Appointment'))

    expect(onClose).toHaveBeenCalledTimes(1)
  })

  it('disables joining and explains why when the waitlist is not enabled', () => {
    renderModal({ waitlistEnabled: false })

    expect(screen.getByText('Join Waitlist')).toBeDisabled()
    expect(screen.getByText('Waitlist is not available for this business.')).toBeInTheDocument()
  })

  it('shows a confirmation instead of the join button once joined', () => {
    renderModal({ joined: true })

    expect(screen.getByText(/You're on the waitlist/)).toBeInTheDocument()
    expect(screen.queryByText('Join Waitlist')).not.toBeInTheDocument()
  })
})
