import { useEffect, useState, type ReactNode } from 'react'
import { t } from '../lib/i18n'

// On-page replacement for window.confirm() -- some browsers (notably WhatsApp's in-app browser,
// see components/customer/InlineConfirm) silently block native dialogs, which makes the guarded
// action do nothing at all. Usage:
//   const [ask, confirmDialog] = useConfirmDialog(lang)
//   if (!(await ask(t(lang, 'deleteConfirm'), { yesLabel: t(lang, 'confirmDeleteYes') }))) return
//   ...and render {confirmDialog} somewhere in the component.
type Options = { yesLabel?: string; noLabel?: string; danger?: boolean }
type Pending = Options & { message: string; resolve: (ok: boolean) => void }

export function useConfirmDialog(lang: string): [(message: string, options?: Options) => Promise<boolean>, ReactNode, boolean] {
  const [pending, setPending] = useState<Pending | null>(null)

  function ask(message: string, options: Options = {}) {
    return new Promise<boolean>((resolve) => setPending({ message, ...options, resolve }))
  }

  function answer(ok: boolean) {
    pending?.resolve(ok)
    setPending(null)
  }

  const dialog = pending && (
    <ConfirmDialog lang={lang} message={pending.message} danger={pending.danger ?? true}
      yesLabel={pending.yesLabel} noLabel={pending.noLabel}
      onYes={() => answer(true)} onNo={() => answer(false)} />
  )
  return [ask, dialog, pending !== null]
}

function ConfirmDialog({
  lang, message, yesLabel, noLabel, danger, onYes, onNo,
}: {
  lang: string
  message: string
  yesLabel?: string
  noLabel?: string
  danger: boolean
  onYes: () => void
  onNo: () => void
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopImmediatePropagation(); onNo() } }
    // Capture phase so an enclosing modal's own Escape handler doesn't also fire.
    window.addEventListener('keydown', onKey, true)
    return () => window.removeEventListener('keydown', onKey, true)
  }, [onNo])

  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'
  return (
    <div onClick={(e) => { e.stopPropagation(); onNo() }} dir={dir}
      className="fixed inset-0 bg-black/60 flex items-center justify-center z-[60] p-4">
      <div role="alertdialog" aria-modal="true" onClick={(e) => e.stopPropagation()}
        className="bg-surface rounded-2xl p-6 max-w-sm w-full border border-line">
        <p className="text-ink mb-5">{message}</p>
        <div className="flex gap-2">
          <button type="button" autoFocus onClick={onYes}
            className={`flex-1 text-white font-semibold py-2.5 rounded-xl transition-colors ${
              danger ? 'bg-red-600 hover:bg-red-700' : 'bg-coral hover:bg-coral-dark'}`}>
            {yesLabel ?? t(lang, 'confirmYes')}
          </button>
          <button type="button" onClick={onNo}
            className="flex-1 border border-line text-ink hover:bg-cream font-medium py-2.5 rounded-xl transition-colors">
            {noLabel ?? t(lang, 'confirmNo')}
          </button>
        </div>
      </div>
    </div>
  )
}
