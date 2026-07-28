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
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" dir={dir}>
      <div className="bg-gray-900 rounded-2xl p-6 max-w-sm w-full border border-gray-800">
        <h2 className="text-white font-semibold text-lg mb-2">{t(lang, 'slotBookedTitle')}</h2>
        {joined ? (
          <p className="text-green-400 text-sm mb-5">{t(lang, 'joinedWaitlistConfirmation')}</p>
        ) : (
          <p className="text-gray-400 text-sm mb-5">{t(lang, 'slotBookedBody')}</p>
        )}
        <div className="space-y-2">
          {!joined && (
            <button type="button" onClick={onJoinWaitlist} disabled={!waitlistEnabled || joining}
              className="w-full bg-blue-600 hover:bg-blue-700 disabled:opacity-50 text-white font-semibold py-2.5 rounded-xl transition-colors">
              {joining ? '...' : t(lang, 'joinWaitlist')}
            </button>
          )}
          {!joined && !waitlistEnabled && (
            <p className="text-gray-500 text-xs text-center">{t(lang, 'waitlistNotAvailable')}</p>
          )}
          <button type="button" onClick={onClose}
            className="w-full bg-gray-800 hover:bg-gray-700 text-gray-200 font-semibold py-2.5 rounded-xl transition-colors">
            {joined ? t(lang, 'chooseAnother') : t(lang, 'chooseAnother')}
          </button>
        </div>
      </div>
    </div>
  )
}
