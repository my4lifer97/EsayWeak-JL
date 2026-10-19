import { useEffect, useRef } from 'react'

// Calls onEscape when Escape is pressed while `active`. The customer modals ignored Escape and
// could only be closed with a button or the backdrop (QA run 3).
export function useEscapeKey(onEscape: () => void, active = true) {
  const latest = useRef(onEscape)
  latest.current = onEscape
  useEffect(() => {
    if (!active) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') latest.current() }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [active])
}
