import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import StarRating from './StarRating'

describe('StarRating', () => {
  it('renders a read-only rating rounded to the nearest star with a count', () => {
    render(<StarRating value={3.6} count={12} />)
    expect(screen.getByLabelText('3.6 out of 5')).toBeInTheDocument()
    expect(screen.getByText('(12)')).toBeInTheDocument()
    // 4 filled + 1 empty
    expect(screen.getByLabelText('3.6 out of 5').textContent).toBe('★★★★☆')
  })

  it('calls onChange with the clicked star value when editable', async () => {
    const onChange = vi.fn()
    render(<StarRating value={0} onChange={onChange} />)
    await userEvent.click(screen.getByLabelText('4 stars'))
    expect(onChange).toHaveBeenCalledWith(4)
  })

  it('does not render a count in editable mode', () => {
    render(<StarRating value={2} count={9} onChange={() => {}} />)
    expect(screen.queryByText('(9)')).not.toBeInTheDocument()
  })
})
