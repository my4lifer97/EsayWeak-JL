import { t } from '../../lib/i18n'
import { useEscapeKey } from '../useEscapeKey'

export default function SlotBookedModal({
  lang, dir, waitlistEnabled, joining, joined, error, onJoinWaitlist, onClose,
}: {
  lang: string
  dir: 'rtl' | 'ltr'
  waitlistEnabled: boolean
  joining: boolean
  joined: boolean
  error?: string
  onJoinWaitlist: () => void
  onClose: () => void
}) {
  useEscapeKey(onClose)
  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" dir={dir}>
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 max-w-sm w-full border border-line">
        <h2 className="text-ink font-semibold text-lg mb-2">{t(lang, 'slotBookedTitle')}</h2>
        {joined ? (
          <p className="text-green-700 dark:text-green-400 text-sm mb-5">{t(lang, 'joinedWaitlistConfirmation')}</p>
        ) : (
          <p className="text-muted text-sm mb-5">{t(lang, 'slotBookedBody')}</p>
        )}
        {error && !joined && (
          <div role="alert" className="bg-red-50 border border-red-200 text-red-700 dark:bg-red-950/40 dark:border-red-800/50 dark:text-red-400 text-sm rounded-lg px-3 py-2 mb-3">{error}</div>
        )}
        <div className="space-y-2">
          {!joined && (
            <button type="button" onClick={onJoinWaitlist} disabled={!waitlistEnabled || joining}
              className="w-full bg-coral hover:bg-coral-dark disabled:opacity-50 text-white font-semibold py-2.5 rounded-xl transition-colors">
              {joining ? '...' : t(lang, 'joinWaitlist')}
            </button>
          )}
          {!joined && !waitlistEnabled && (
            <p className="text-muted text-xs text-center">{t(lang, 'waitlistNotAvailable')}</p>
          )}
          <button type="button" onClick={onClose}
            className="w-full bg-teal-tint hover:bg-teal-tint/70 text-ink font-semibold py-2.5 rounded-xl transition-colors">
            {t(lang, 'chooseAnother')}
          </button>
        </div>
      </div>
    </div>
  )
}
