import { t } from '../../lib/i18n'

export default function BookingSuccessModal({ lang, dir, onClose }: { lang: string; dir: 'rtl' | 'ltr'; onClose: () => void }) {
  return (
    <div onClick={onClose} className="fixed inset-0 bg-black/60 flex items-center justify-center z-50 p-4" dir={dir}>
      <div onClick={(e) => e.stopPropagation()} className="bg-surface rounded-2xl p-6 max-w-sm w-full border border-line relative">
        <button type="button" onClick={onClose} aria-label={t(lang, 'close')}
          className="absolute top-4 end-4 text-muted hover:text-ink transition-colors text-xl leading-none">
          &times;
        </button>
        <div className="text-green-600 dark:text-green-400 text-4xl mb-3">✓</div>
        <h2 className="text-ink font-semibold text-lg mb-2">{t(lang, 'bookingSuccessTitle')}</h2>
        <p className="text-muted text-sm mb-5">{t(lang, 'bookingSuccessBody')}</p>
        <button type="button" onClick={onClose}
          className="w-full bg-coral hover:bg-coral-dark text-white font-semibold py-2.5 rounded-xl transition-colors">
          {t(lang, 'close')}
        </button>
      </div>
    </div>
  )
}
