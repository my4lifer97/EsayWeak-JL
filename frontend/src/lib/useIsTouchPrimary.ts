import { useEffect, useState } from 'react'

// (pointer: coarse) + (hover: none) identifies a touch-primary device (phone/tablet), unlike a
// width-based CSS breakpoint which also flips on a desktop browser whenever its window is
// narrow or the page is zoomed in -- both report a small CSS viewport width on a PC that still
// has a mouse. This is a hardware/input-capability query, not a size query, so it stays stable
// regardless of window width, browser zoom, or OS display scaling.
const QUERY = '(pointer: coarse) and (hover: none)'

export function useIsTouchPrimary(): boolean {
  const [isTouch, setIsTouch] = useState(() => window.matchMedia(QUERY).matches)

  useEffect(() => {
    const mq = window.matchMedia(QUERY)
    const onChange = () => setIsTouch(mq.matches)
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [])

  return isTouch
}
