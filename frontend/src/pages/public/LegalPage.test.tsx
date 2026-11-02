import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import LegalPage from './LegalPage'
import { useCustomerAuth } from '../../lib/customerAuth'
import { ConsentCheckbox } from '../../components/LegalConsent'

vi.mock('../../lib/customerAuth', () => ({ useCustomerAuth: vi.fn() }))
vi.mock('../../components/customer/LanguageSwitcher', () => ({ default: () => null }))

describe('LegalPage', () => {
  it('shows the privacy policy in the customer language, rtl for Hebrew', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ language: 'HE' } as ReturnType<typeof useCustomerAuth>)
    const { container } = render(<MemoryRouter><LegalPage doc="privacy" /></MemoryRouter>)
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('מדיניות פרטיות')
    expect(container.firstElementChild).toHaveAttribute('dir', 'rtl')
    expect(screen.getAllByRole('link', { name: 'הצהרת נגישות' })[0]).toHaveAttribute('href', '/accessibility')
  })

  it('shows English terms', () => {
    vi.mocked(useCustomerAuth).mockReturnValue({ language: 'EN' } as ReturnType<typeof useCustomerAuth>)
    render(<MemoryRouter><LegalPage doc="terms" /></MemoryRouter>)
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Terms of Use')
  })
})

describe('ConsentCheckbox', () => {
  it('is required and links both documents', () => {
    render(<MemoryRouter><ConsentCheckbox lang="EN" checked={false} onChange={() => {}} /></MemoryRouter>)
    expect(screen.getByRole('checkbox')).toBeRequired()
    expect(screen.getByRole('link', { name: 'Terms of Use' })).toHaveAttribute('href', '/terms')
    expect(screen.getByRole('link', { name: 'Privacy Policy' })).toHaveAttribute('href', '/privacy')
  })
})
