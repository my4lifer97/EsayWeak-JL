import { t } from '../../lib/i18n'

export default function BookingSuccessModal({ lang, dir, onClose }: { lang: string; dir: 'rtl' | 'ltr'; onClose: () => void }) {
  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" dir={dir}>
      <div onClick={(e) => e.stopPropagation()} className="bg-gray-900 rounded-2xl p-6 max-w-sm w-full border border-gray-800 relative">
        <button type="button" onClick={onClose} aria-label={t(lang, 'close')}
          className="absolute top-4 end-4 text-gray-500 hover:text-white transition-colors text-xl leading-none">
          &times;
        </button>
        <div className="text-green-400 text-4xl mb-3">✓</div>
        <h2 className="text-white font-semibold text-lg mb-2">{t(lang, 'bookingSuccessTitle')}</h2>
        <p className="text-gray-400 text-sm mb-5">{t(lang, 'bookingSuccessBody')}</p>
        <button type="button" onClick={onClose}
          className="w-full bg-blue-600 hover:bg-blue-700 text-white font-semibold py-2.5 rounded-xl transition-colors">
          {t(lang, 'close')}
        </button>
      </div>
    </div>
  )
}
