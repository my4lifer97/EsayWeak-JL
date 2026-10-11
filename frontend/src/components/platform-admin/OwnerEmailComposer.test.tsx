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

    await waitFor(() => expect(platformAdminApi.post).toHaveBeenCalledWith(
      '/platform-admin/businesses/b1/reset-password', { temporary: true, newPassword: 'Tmp-abc12345', silent: true }))
  })
})
