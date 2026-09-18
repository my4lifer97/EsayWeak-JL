import { t } from '../../lib/i18n'

export default function SlotBookedModal({
  lang, dir, waitlistEnabled, joining, joined, onJoinWaitlist, onClose,
}: {
  lang: string
  dir: 'rtl' | 'ltr'
  waitlistEnabled: boolean
  joining: boolean
  joined: boolean
  onJoinWaitlist: () => void
  onClose: () => void
}) {
  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" dir={dir}>
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 max-w-sm w-full border border-line">
        <h2 className="text-ink font-semibold text-lg mb-2">{t(lang, 'slotBookedTitle')}</h2>
        {joined ? (
          <p className="text-green-700 dark:text-green-400 text-sm mb-5">{t(lang, 'joinedWaitlistConfirmation')}</p>
        ) : (
          <p className="text-muted text-sm mb-5">{t(lang, 'slotBookedBody')}</p>
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
            {joined ? t(lang, 'chooseAnother') : t(lang, 'chooseAnother')}
          </button>
        </div>
      </div>
    </div>
  )
}
