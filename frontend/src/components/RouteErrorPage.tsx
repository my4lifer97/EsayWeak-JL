import { t } from '../lib/i18n'

// The root route's errorElement. A page chunk that still fails after lazyPage's one reload (or
// any render error) used to show React Router's raw "Unexpected Application Error" screen.
export default function RouteErrorPage() {
  let lang = 'EN'
  try {
    const path = window.location.pathname
    const owner = path.startsWith('/admin') || path.startsWith('/platform-admin')
    lang = (owner ? localStorage.getItem('lang') : localStorage.getItem('customerLang')) ?? (owner ? 'EN' : 'HE')
  } catch { /* storage blocked */ }
  return (
    <div className="min-h-screen bg-cream text-ink flex items-center justify-center px-4" dir={lang === 'AR' || lang === 'HE' ? 'rtl' : 'ltr'}>
      <div className="text-center space-y-4 max-w-sm">
        <p className="text-muted">{t(lang, 'pageLoadFailed')}</p>
        <button type="button" onClick={() => window.location.reload()}
          className="bg-coral hover:bg-coral-dark text-white font-semibold px-5 py-2.5 rounded-xl transition-colors">
          {t(lang, 'reloadPage')}
        </button>
      </div>
    </div>
  )
}
