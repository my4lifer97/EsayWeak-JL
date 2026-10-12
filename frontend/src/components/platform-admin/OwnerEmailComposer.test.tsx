import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import OwnerEmailComposer from './OwnerEmailComposer'
import { platformAdminApi } from '../../lib/platformAdminApi'

vi.mock('../../lib/platformAdminApi', () => ({
  platformAdminApi: { post: vi.fn() },
}))

function renderComposer() {
  return render(
    <OwnerEmailComposer businessId="b1" businessName="Shop" businessEmail="owner@example.com" username="ownersh" onClose={() => {}} />
  )
}

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
})

// Generating used to reset the real password at once -- a closed composer or failed Gmail send
// then left the owner locked out with a password nobody received.
describe('OwnerEmailComposer temp password', () => {
  it('generating does not reset the account; sending carries the password to apply', async () => {
    vi.mocked(platformAdminApi.post).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/generate-temp-password') ? { tempPassword: 'Tmp-abc12345' } : { ok: true } }))
    renderComposer()

    await userEvent.click(screen.getByText('Generate new temporary password'))
    await screen.findByText(/Not active yet/)

    expect(platformAdminApi.post).toHaveBeenCalledWith('/platform-admin/businesses/b1/generate-temp-password')
    expect(platformAdminApi.post).not.toHaveBeenCalledWith(expect.stringContaining('reset-password'), expect.anything())

    await userEvent.click(screen.getByText('Send Email'))

    await waitFor(() => expect(platformAdminApi.post).toHaveBeenCalledWith(
      '/platform-admin/businesses/b1/email',
      expect.objectContaining({ newTempPassword: 'Tmp-abc12345', body: expect.stringContaining('Tmp-abc12345') })))
    expect(await screen.findByText(/Active — this is now the owner's password/)).toBeInTheDocument()
  })

  it('a failed send leaves the password marked as not active', async () => {
    vi.mocked(platformAdminApi.post).mockImplementation((url: string) =>
      url.endsWith('/generate-temp-password')
        ? Promise.resolve({ data: { tempPassword: 'Tmp-abc12345' } })
        : Promise.reject({ response: { data: { error: "Could not send the email. The owner's password was NOT changed." } } }))
    renderComposer()

    await userEvent.click(screen.getByText('Generate new temporary password'))
    await userEvent.click(await screen.findByText('Send Email'))

    expect(await screen.findByText(/password was NOT changed/)).toBeInTheDocument()
    expect(screen.getByText(/Not active yet/)).toBeInTheDocument()
  })

  it('"Set password now" applies exactly the shown password', async () => {
    vi.mocked(platformAdminApi.post).mockImplementation((url: string) =>
      Promise.resolve({ data: url.endsWith('/generate-temp-password') ? { tempPassword: 'Tmp-abc12345' } : {} }))
    renderComposer()

    await userEvent.click(screen.getByText('Generate new temporary password'))
    await userEvent.click(await screen.findByText('Set password now'))
    // Asks first, since it changes the owner's real password.
    expect(platformAdminApi.post).not.toHaveBeenCalledWith(expect.stringContaining('reset-password'), expect.anything())
    await userEvent.click(screen.getByRole('button', { name: 'Set password' }))

    await waitFor(() => expect(platformAdminApi.post).toHaveBeenCalledWith(
      '/platform-admin/businesses/b1/reset-password', { temporary: true, newPassword: 'Tmp-abc12345', silent: true }))
  })
})

// QA: after approval the only copy of the temp password is in the composer -- closing it by
// accident lost the owner's login for good.
describe('OwnerEmailComposer close guard', () => {
  function renderWithApprovedPassword(onClose: () => void) {
    return render(
      <OwnerEmailComposer businessId="b1" businessName="Shop" businessEmail="owner@example.com"
        username="ownersh" initialPassword="Tmp-approved1" onClose={onClose} />
    )
  }

  it('asks before closing when the active password was never sent or copied', async () => {
    const onClose = vi.fn()
    renderWithApprovedPassword(onClose)

    await userEvent.click(screen.getByLabelText('Close'))
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByText(/can't be shown again/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: 'Close anyway' }))
    expect(onClose).toHaveBeenCalled()
  })

  it('closes without asking once the email was sent', async () => {
    vi.mocked(platformAdminApi.post).mockResolvedValue({ data: {} })
    const onClose = vi.fn()
    renderWithApprovedPassword(onClose)

    await userEvent.click(screen.getByText('Send Email'))
    await screen.findByText('Sent ✓')
    await userEvent.click(screen.getByLabelText('Close'))

    expect(onClose).toHaveBeenCalled()
  })

  it('includes a sign-in link with the login details', () => {
    renderWithApprovedPassword(() => {})
    expect(screen.getByDisplayValue(/Password: Tmp-approved1\s+Sign in at: .*\/admin\/login/)).toBeInTheDocument()
  })
})
