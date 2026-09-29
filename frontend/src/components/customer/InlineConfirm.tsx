import { t } from '../../lib/i18n'

// On-page "are you sure?" instead of window.confirm() -- the in-app browser WhatsApp opens links
// in (and some other mobile webviews) silently suppresses native dialogs, which made confirm()
// return false so tapping Cancel appeared to do nothing at all.
export default function InlineConfirm({
  lang, question, busy, onYes, onNo, yesLabel, noLabel,
}: {
  lang: string
  question: string
  yesLabel?: string
  noLabel?: string
  busy: boolean
  onYes: () => void
  onNo: () => void
}) {
  return (
    <div className="border border-red-200 dark:border-red-800/50 bg-red-50 dark:bg-red-950/40 rounded-xl p-3 space-y-2">
      <p className="text-sm text-ink">{question}</p>
      <div className="flex gap-2">
        <button type="button" disabled={busy} onClick={onYes}
          className="flex-1 bg-red-600 hover:bg-red-700 text-white text-sm font-semibold py-2 rounded-lg transition-colors disabled:opacity-50">
          {busy ? '...' : yesLabel ?? t(lang, 'confirmCancelYes')}
        </button>
        <button type="button" disabled={busy} onClick={onNo}
          className="flex-1 border border-line bg-surface text-ink hover:bg-cream text-sm font-medium py-2 rounded-lg transition-colors disabled:opacity-50">
          {noLabel ?? t(lang, 'confirmCancelNo')}
        </button>
      </div>
    </div>
  )
}
