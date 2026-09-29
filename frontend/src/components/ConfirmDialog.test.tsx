import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useEffect, useState } from 'react'
import { useConfirmDialog } from './ConfirmDialog'

// A stand-in for a modal like PresetEditorModal: it listens for Escape itself and asks before acting.
function Harness({ onOuterEscape }: { onOuterEscape: () => void }) {
  const [ask, dialog] = useConfirmDialog('EN')
  const [result, setResult] = useState('')
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onOuterEscape() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  })
  return (
    <div>
      <button onClick={async () => setResult(String(await ask('Really?', { yesLabel: 'Yes, delete' })))}>Delete</button>
      <span>result:{result}</span>
      {dialog}
    </div>
  )
}

describe('useConfirmDialog', () => {
  it('resolves true on Yes and false on No, without window.confirm', async () => {
    const confirmSpy = vi.spyOn(window, 'confirm')
    render(<Harness onOuterEscape={vi.fn()} />)

    await userEvent.click(screen.getByText('Delete'))
    expect(screen.getByText('Really?')).toBeInTheDocument()
    await userEvent.click(screen.getByText('Yes, delete'))
    await waitFor(() => expect(screen.getByText('result:true')).toBeInTheDocument())
    expect(screen.queryByText('Really?')).not.toBeInTheDocument()

    await userEvent.click(screen.getByText('Delete'))
    await userEvent.click(screen.getByText('No'))
    await waitFor(() => expect(screen.getByText('result:false')).toBeInTheDocument())
    expect(confirmSpy).not.toHaveBeenCalled()
  })

  it('Escape answers No and does not also reach the enclosing modal', async () => {
    const onOuterEscape = vi.fn()
    render(<Harness onOuterEscape={onOuterEscape} />)

    await userEvent.click(screen.getByText('Delete'))
    await userEvent.keyboard('{Escape}')

    await waitFor(() => expect(screen.getByText('result:false')).toBeInTheDocument())
    expect(onOuterEscape).not.toHaveBeenCalled()
  })
})
