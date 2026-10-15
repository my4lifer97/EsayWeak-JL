import { useEffect, type ReactNode } from 'react'
import { useBlocker } from 'react-router-dom'
import { useConfirmDialog } from '../ConfirmDialog'
import { t } from '../../lib/i18n'

// Asks before leaving a page with unsaved edits (QA run 2: Settings/Services edits were dropped
// silently on navigation). Covers in-app navigation (sidebar links, browser back) via the data
// router's useBlocker, and closing/reloading the tab via beforeunload (the browser shows its own
// generic prompt there). Signing out (-> /admin/login) is never blocked.
// Render the returned element somewhere in the page.
export function useUnsavedChangesGuard(dirty: boolean, lang: string): ReactNode {
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    dirty && currentLocation.pathname !== nextLocation.pathname && nextLocation.pathname !== '/admin/login')
  const [ask, dialog] = useConfirmDialog(lang)

  useEffect(() => {
    if (blocker.state !== 'blocked') return
    ask(t(lang, 'unsavedChangesLeave'), { yesLabel: t(lang, 'unsavedChangesLeaveYes'), noLabel: t(lang, 'unsavedChangesStay') })
      .then((leave) => (leave ? blocker.proceed() : blocker.reset()))
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [blocker.state])

  useEffect(() => {
    if (!dirty) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = '' }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [dirty])

  return dialog
}
