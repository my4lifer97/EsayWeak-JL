import { t } from '../../lib/i18n'

// Covers the page once another tab of this browser changed the owner login (see AuthProvider).
// This tab still acts as its own account until it reloads, but its sidebar and banner would be out
// of date, so the only way on is a reload.
export default function SessionChangedNotice({ lang }: { lang: string }) {
  const dir = lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'
  return (
    <div className="fixed inset-0 bg-black/60 flex items-center justify-center z-[70] p-4">
      <div role="alertdialog" aria-modal="true" dir={dir}
        className="bg-surface rounded-2xl p-6 max-w-sm w-full border border-line">
        <p className="text-ink mb-5">{t(lang, 'sessionChangedElsewhere')}</p>
        <button type="button" onClick={() => window.location.reload()}
          className="w-full bg-coral hover:bg-coral-dark text-white font-semibold text-sm py-2.5 rounded-lg transition-colors">
          {t(lang, 'reloadPage')}
        </button>
      </div>
    </div>
  )
}
